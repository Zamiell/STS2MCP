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

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class McpActionNoteAttribute : Attribute
{
    public McpActionNoteAttribute(string note)
    {
        Note = note;
    }

    public string Note { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class McpEndpointAttribute : Attribute
{
    public McpEndpointAttribute(string method, string path, string description)
    {
        Method = method;
        Path = path;
        Description = description;
    }

    public string Method { get; }
    public string Path { get; }
    public string Description { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class McpQueryParameterAttribute : Attribute
{
    public McpQueryParameterAttribute(
        string endpoint,
        string name,
        string values,
        string defaultValue,
        string description)
    {
        Endpoint = endpoint;
        Name = name;
        Values = values;
        DefaultValue = defaultValue;
        Description = description;
    }

    public string Endpoint { get; }
    public string Name { get; }
    public string Values { get; }
    public string DefaultValue { get; }
    public string Description { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class McpStateTypeAttribute : Attribute
{
    public McpStateTypeAttribute(
        string stateType,
        string screen,
        string availableActions,
        string description)
    {
        StateType = stateType;
        Screen = screen;
        AvailableActions = availableActions;
        Description = description;
    }

    public string StateType { get; }
    public string Screen { get; }
    public string AvailableActions { get; }
    public string Description { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class McpStateExampleAttribute : Attribute
{
    public McpStateExampleAttribute(
        string stateType,
        string description,
        string json)
    {
        StateType = stateType;
        Description = description;
        Json = json;
    }

    public string StateType { get; }
    public string Description { get; }
    public string Json { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class McpDocSectionAttribute : Attribute
{
    public McpDocSectionAttribute(int order, string title, string body)
    {
        Order = order;
        Title = title;
        Body = body;
    }

    public int Order { get; }
    public string Title { get; }
    public string Body { get; }
}
