namespace Console2Lce;

public sealed class SavegameDatNotFoundException : Console2LceException
{
    public SavegameDatNotFoundException()
        : base("The STFS package does not contain savegame.dat.")
    {
    }
}
