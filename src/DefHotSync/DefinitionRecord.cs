namespace DefHotSync;

public sealed record DefinitionRecord(
    string Id,
    int Version,
    string Format,
    string Body);
