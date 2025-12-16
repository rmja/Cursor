using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Cursor.SourceGenerators;

[Generator]
public class CursorPaginationEnumeratorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find methods with [GenerateEnumerator] attribute
        var methodsToGenerate = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                "Cursor.GenerateEnumeratorAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (context, cancellationToken) =>
                    GetMethodInfo(context, cancellationToken)
            )
            .Where(static m => m != null)
            .Collect();

        // Generate extension methods grouped by interface
        context.RegisterSourceOutput(
            methodsToGenerate,
            static (context, methods) => GenerateExtensionMethods(context, methods)
        );
    }

    private static MethodInfo? GetMethodInfo(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.TargetSymbol is not IMethodSymbol methodSymbol)
            return null;

        // Validate return type is Task<ICursorPage<T>>
        if (!IsTaskOfICursorPage(methodSymbol.ReturnType, out var itemType, out var pageType))
            return null;

        // Validate method name follows pattern List*Async
        var methodName = methodSymbol.Name;
        if (!methodName.StartsWith("List") || !methodName.EndsWith("Async"))
            return null;

        // Get parameter names from the attribute
        var attributeData = context.Attributes.FirstOrDefault();
        var limitParamName = GetAttributeProperty(attributeData, "LimitParameterName") ?? "limit";
        var cursorParamName = GetAttributeProperty(attributeData, "CursorParameterName") ?? "cursor";

        // Find limit, cursor, and cancellationToken parameters
        var comparer = SymbolEqualityComparer.Default;
        var limitParam = methodSymbol.Parameters.FirstOrDefault(p => p.Name == limitParamName);
        var cursorParam = methodSymbol.Parameters.FirstOrDefault(p => p.Name == cursorParamName);
        var ctParam = methodSymbol.Parameters.FirstOrDefault(p => p.Name == "cancellationToken");

        if (limitParam == null || cursorParam == null || ctParam == null)
            return null;

        // Get containing interface
        var containingType = methodSymbol.ContainingType;
        if (containingType.TypeKind != TypeKind.Interface)
            return null;

        // Get all parameters except limit, cursor, and cancellationToken
        // Use SymbolEqualityComparer for proper symbol comparison
        var excludedParams = new HashSet<IParameterSymbol>(comparer)
        {
            limitParam,
            cursorParam,
            ctParam,
        };

        var enumeratorParams = methodSymbol
            .Parameters.Where(p => !excludedParams.Contains(p, comparer))
            .ToImmutableArray();

        // Generate enumerator method name: List*Async -> Enumerate*Async
        var baseName = methodName.Substring(4, methodName.Length - 9); // Remove "List" and "Async"
        var enumeratorMethodName = $"Enumerate{baseName}Async";
        var pagesEnumeratorMethodName = $"Enumerate{baseName}PagesAsync";

        // Get the type of the limit parameter
        // If it's already nullable (e.g., int?), use as-is; otherwise make it nullable
        var limitType = limitParam.Type;
        var limitTypeString = limitType.ToDisplayString();
        var isAlreadyNullable =
            limitType.NullableAnnotation == NullableAnnotation.Annotated
            || limitType.IsReferenceType;

        return new MethodInfo(
            interfaceName: containingType.ToDisplayString(),
            interfaceNamespace: containingType.ContainingNamespace.ToDisplayString(),
            originalMethodName: methodName,
            enumeratorMethodName: enumeratorMethodName,
            pagesEnumeratorMethodName: pagesEnumeratorMethodName,
            itemType: itemType!.ToDisplayString(),
            pageType: pageType!.ToDisplayString(),
            limitType: limitTypeString,
            isLimitAlreadyNullable: isAlreadyNullable,
            parameters: enumeratorParams,
            allParameters: methodSymbol.Parameters.ToImmutableArray(),
            limitParameterName: limitParamName,
            cursorParameterName: cursorParamName
        );
    }

    private static string? GetAttributeProperty(AttributeData? attributeData, string propertyName)
    {
        if (attributeData == null)
            return null;

        var namedArg = attributeData.NamedArguments.FirstOrDefault(kvp => kvp.Key == propertyName);
        if (namedArg.Value.Value is string stringValue)
            return stringValue;

        return null;
    }

    private static bool IsTaskOfICursorPage(ITypeSymbol returnType, out ITypeSymbol? itemType, out ITypeSymbol? pageType)
    {
        itemType = null;
        pageType = null;

        if (returnType is not INamedTypeSymbol { Name: "Task", TypeArguments.Length: 1 } taskType)
            return false;

        var innerType = taskType.TypeArguments[0];
        pageType = innerType;

        // Check if the type implements ICursorPage<T>
        // First check if the type itself is ICursorPage<T>
        if (innerType is INamedTypeSymbol { Name: "ICursorPage" } namedType &&
            namedType.ContainingNamespace?.ToDisplayString() == "Cursor" &&
            namedType.TypeArguments.Length == 1)
        {
            itemType = namedType.TypeArguments[0];
            return true;
        }

        // Then check if it implements ICursorPage<T>
        var cursorPageInterface = innerType.AllInterfaces
            .FirstOrDefault(i => 
                i.Name == "ICursorPage" && 
                i.ContainingNamespace?.ToDisplayString() == "Cursor" &&
                i.TypeArguments.Length == 1);

        if (cursorPageInterface == null)
            return false;

        itemType = cursorPageInterface.TypeArguments[0];
        return true;
    }

    private static void GenerateExtensionMethods(
        SourceProductionContext context,
        ImmutableArray<MethodInfo?> methods
    )
    {
        if (methods.IsDefaultOrEmpty)
            return;

        // Group by interface
        var groupedByInterface = methods
            .Where(static m => m != null)
            .GroupBy(static m => (m!.InterfaceName, m.InterfaceNamespace));

        foreach (var group in groupedByInterface)
        {
            var (interfaceName, interfaceNamespace) = group.Key;
            var interfaceShortName = interfaceName.Split('.').Last();

            // Remove 'I' prefix if present (e.g., ISalesClient -> SalesClient)
            var extensionClassName =
                interfaceShortName.StartsWith("I") && interfaceShortName.Length > 1
                    ? interfaceShortName.Substring(1) + "Extensions"
                    : interfaceShortName + "Extensions";

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using Cursor;");
            sb.AppendLine();
            sb.AppendLine($"namespace {interfaceNamespace};");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine(
                $"/// Extension methods for <see cref=\"{interfaceShortName}\"/> providing cursor pagination enumerators."
            );
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public static partial class {extensionClassName}");
            sb.AppendLine("{");

            foreach (var method in group)
            {
                if (method == null)
                    continue;

                // Generate item enumerator (flattens pages)
                GenerateItemEnumeratorMethod(sb, interfaceShortName, method);

                // Generate page enumerator (yields pages)
                GeneratePageEnumeratorMethod(sb, interfaceShortName, method);
            }

            sb.AppendLine("}");

            context.AddSource(
                $"{extensionClassName}.g.cs",
                SourceText.From(sb.ToString(), Encoding.UTF8)
            );
        }
    }

    private static void GenerateItemEnumeratorMethod(
        StringBuilder sb,
        string interfaceShortName,
        MethodInfo method
    )
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine(
            $"    /// Enumerates all {EscapeXmlDoc(method.ItemType)} instances by automatically handling pagination."
        );
        sb.AppendLine("    /// </summary>");

        // Generate parameter documentation
        foreach (var param in method.Parameters)
        {
            sb.AppendLine(
                $"    /// <param name=\"{param.Name}\">{EscapeXmlDoc(param.Name)}</param>"
            );
        }
        sb.AppendLine(
            "    /// <param name=\"pageSize\">Optional page size for each request. If null, uses server default.</param>"
        );
        sb.AppendLine(
            "    /// <param name=\"maxPages\">Optional maximum number of pages to fetch. If null, fetches all available pages.</param>"
        );

        sb.AppendLine(
            $"    /// <returns>An async enumerable of {EscapeXmlDoc(method.ItemType)} instances.</returns>"
        );
        sb.Append(
            $"    public static IAsyncEnumerable<{method.ItemType}> {method.EnumeratorMethodName}("
        );
        sb.AppendLine();
        sb.Append($"        this {interfaceShortName} client");

        // Add parameters (excluding limit, cursor, cancellationToken)
        foreach (var param in method.Parameters)
        {
            sb.AppendLine(",");
            sb.Append($"        {GetParameterDeclaration(param)}");
        }

        // Add pageSize parameter - use the limit type as-is if already nullable, otherwise make it nullable
        sb.AppendLine(",");
        var pageSizeType = method.IsLimitAlreadyNullable
            ? method.LimitType
            : $"{method.LimitType}?";
        sb.Append($"        {pageSizeType} pageSize = null");

        // Add maxPages parameter
        sb.AppendLine(",");
        sb.Append("        int? maxPages = null");

        sb.AppendLine();
        sb.AppendLine("    ) =>");
        sb.AppendLine($"        new CursorPaginationEnumerable<{method.ItemType}, {method.PageType}>(");
        sb.AppendLine("            (cursor, cancellationToken) =>");
        sb.Append($"                client.{method.OriginalMethodName}(");

        // Call original method with all parameters
        var paramNames = BuildParameterCallList(method, "pageSize");

        sb.AppendLine();
        sb.Append("                    ");
        sb.Append(string.Join(",\r\n                    ", paramNames));
        sb.AppendLine();
        sb.AppendLine("                )");
        sb.AppendLine("        ,   maxPages");
        sb.AppendLine("        );");
        sb.AppendLine();
    }

    private static void GeneratePageEnumeratorMethod(
        StringBuilder sb,
        string interfaceShortName,
        MethodInfo method
    )
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine(
            $"    /// Enumerates pages of {EscapeXmlDoc(method.ItemType)} instances by automatically handling pagination."
        );
        sb.AppendLine("    /// </summary>");

        // Generate parameter documentation
        foreach (var param in method.Parameters)
        {
            sb.AppendLine(
                $"    /// <param name=\"{param.Name}\">{EscapeXmlDoc(param.Name)}</param>"
            );
        }
        sb.AppendLine(
            "    /// <param name=\"pageSize\">Optional page size for each request. If null, uses server default.</param>"
        );
        sb.AppendLine(
            "    /// <param name=\"maxPages\">Optional maximum number of pages to fetch. If null, fetches all available pages.</param>"
        );

        sb.AppendLine(
            $"    /// <returns>An async enumerable of pages containing {EscapeXmlDoc(method.ItemType)} instances.</returns>"
        );
        sb.Append(
            $"    public static IAsyncEnumerable<{method.PageType}> {method.PagesEnumeratorMethodName}("
        );
        sb.AppendLine();
        sb.Append($"        this {interfaceShortName} client");

        // Add parameters (excluding limit, cursor, cancellationToken)
        foreach (var param in method.Parameters)
        {
            sb.AppendLine(",");
            sb.Append($"        {GetParameterDeclaration(param)}");
        }

        // Add pageSize parameter - use the limit type as-is if already nullable, otherwise make it nullable
        sb.AppendLine(",");
        var pageSizeType = method.IsLimitAlreadyNullable
            ? method.LimitType
            : $"{method.LimitType}?";
        sb.Append($"        {pageSizeType} pageSize = null");

        // Add maxPages parameter
        sb.AppendLine(",");
        sb.Append("        int? maxPages = null");

        sb.AppendLine();
        sb.AppendLine("    ) =>");
        sb.AppendLine($"        new CursorPaginationPageEnumerable<{method.ItemType}, {method.PageType}>(");
        sb.AppendLine("            (cursor, cancellationToken) =>");
        sb.Append($"                client.{method.OriginalMethodName}(");

        // Call original method with all parameters
        var paramNames = BuildParameterCallList(method, "pageSize");

        sb.AppendLine();
        sb.Append("                    ");
        sb.Append(string.Join(",\r\n                    ", paramNames));
        sb.AppendLine();
        sb.AppendLine("                )");
        sb.AppendLine("        ,   maxPages");
        sb.AppendLine("        );");
        sb.AppendLine();
    }

    private static List<string> BuildParameterCallList(MethodInfo method, string pageSizeParamName)
    {
        var paramNames = new List<string>(method.AllParameters.Length);

        foreach (var param in method.AllParameters)
        {
            if (param.Name == method.LimitParameterName)
            {
                paramNames.Add(pageSizeParamName);
            }
            else if (param.Name == method.CursorParameterName)
            {
                paramNames.Add("cursor");
            }
            else if (param.Name == "cancellationToken")
            {
                paramNames.Add("cancellationToken");
            }
            else
            {
                paramNames.Add(param.Name);
            }
        }

        return paramNames;
    }

    private static string GetParameterDeclaration(IParameterSymbol param)
    {
        var sb = new StringBuilder();

        sb.Append(param.Type.ToDisplayString());
        sb.Append(' ');
        sb.Append(param.Name);

        if (param.HasExplicitDefaultValue)
        {
            sb.Append(" = ");
            if (param.ExplicitDefaultValue == null)
            {
                sb.Append("null");
            }
            else if (param.ExplicitDefaultValue is string stringValue)
            {
                sb.Append($"\"{stringValue}\"");
            }
            else if (param.ExplicitDefaultValue is bool boolValue)
            {
                sb.Append(boolValue ? "true" : "false");
            }
            else
            {
                sb.Append(param.ExplicitDefaultValue);
            }
        }

        return sb.ToString();
    }

    private static string EscapeXmlDoc(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private sealed record MethodInfo
    {
        public string InterfaceName { get; }
        public string InterfaceNamespace { get; }
        public string OriginalMethodName { get; }
        public string EnumeratorMethodName { get; }
        public string PagesEnumeratorMethodName { get; }
        public string ItemType { get; }
        public string PageType { get; }
        public string LimitType { get; }
        public bool IsLimitAlreadyNullable { get; }
        public ImmutableArray<IParameterSymbol> Parameters { get; }
        public ImmutableArray<IParameterSymbol> AllParameters { get; }
        public string LimitParameterName { get; }
        public string CursorParameterName { get; }

        public MethodInfo(
            string interfaceName,
            string interfaceNamespace,
            string originalMethodName,
            string enumeratorMethodName,
            string pagesEnumeratorMethodName,
            string itemType,
            string pageType,
            string limitType,
            bool isLimitAlreadyNullable,
            ImmutableArray<IParameterSymbol> parameters,
            ImmutableArray<IParameterSymbol> allParameters,
            string limitParameterName,
            string cursorParameterName
        )
        {
            InterfaceName = interfaceName;
            InterfaceNamespace = interfaceNamespace;
            OriginalMethodName = originalMethodName;
            EnumeratorMethodName = enumeratorMethodName;
            PagesEnumeratorMethodName = pagesEnumeratorMethodName;
            ItemType = itemType;
            PageType = pageType;
            LimitType = limitType;
            IsLimitAlreadyNullable = isLimitAlreadyNullable;
            Parameters = parameters;
            AllParameters = allParameters;
            LimitParameterName = limitParameterName;
            CursorParameterName = cursorParameterName;
        }
    }
}
