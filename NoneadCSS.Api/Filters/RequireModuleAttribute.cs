namespace NoneadCSS.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireModuleAttribute : Attribute
{
    public string ModuleName { get; }

    public RequireModuleAttribute(string moduleName)
    {
        ModuleName = moduleName;
    }
}