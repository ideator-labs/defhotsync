namespace DefHotSync;

public interface IDefinitionStore
{
    DefinitionRecord? TryGet(string id, int? version = null);
}
