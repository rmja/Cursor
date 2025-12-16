using Cursor;

namespace Example;

// Or by deriving from the attribute
public class MyCustomEnumeratorAttribute : GenerateEnumeratorAttribute
{
    public MyCustomEnumeratorAttribute()
    {
        LimitParameterName = "pageSize";
        CursorParameterName = "nextToken";
    }
}
