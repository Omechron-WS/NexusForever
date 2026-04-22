namespace NexusForever.Game.Static.Spell
{
    /// <summary>
    /// Determines the spell casting behaviour variant.
    /// Used by the spell factory to instantiate the correct spell subtype.
    /// </summary>
    public enum CastMethod
    {
        Normal                = 0x0000,
        Channeled             = 0x0001,
        PressHold             = 0x0002,
        ChanneledField        = 0x0003,
        Unused04              = 0x0004,
        ClientSideInteraction = 0x0005,
        RapidTap              = 0x0006,
        ChargeRelease         = 0x0007,
        Multiphase            = 0x0008,
        Transactional         = 0x0009,
        Aura                  = 0x000A
    }
}
