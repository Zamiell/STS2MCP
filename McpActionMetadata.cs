using System;

namespace STS2_MCP;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class McpActionAttribute : Attribute
{
    public McpActionAttribute(string action, string category, string description)
    {
        Action = action;
        Category = category;
        Description = description;
    }

    public string Action { get; }
    public string Category { get; }
    public string Description { get; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class McpActionFieldAttribute : Attribute
{
    public McpActionFieldAttribute(
        string name,
        string type,
        bool required,
        string description)
    {
        Name = name;
        Type = type;
        Required = required;
        Description = description;
    }

    public string Name { get; }
    public string Type { get; }
    public bool Required { get; }
    public string Description { get; }
}
