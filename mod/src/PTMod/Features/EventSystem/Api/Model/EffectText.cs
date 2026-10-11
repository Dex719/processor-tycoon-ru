namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// A line about an effect for the mail, the window and the journal: the key of a string table of the effect's feature and the arguments
    /// (already formatted for the player), built only from the numbers saved in the <see cref="AppliedEffect"/>.
    /// </summary>
    public struct EffectText
    {
        public string Key;
        public string[] Args;

        public EffectText(string key, params string[] args)
        {
            Key = key;
            Args = args;
        }
    }
}
