namespace NexusForever.Game.Static.Combat
{
    /// <summary>
    /// Event types that can trigger a proc spell cast.
    /// </summary>
    public enum ProcType
    {
        BeginMoving      = 11,
        OnHit            = 12,
        OnDamageReceived = 16,
        CriticalDamage   = 145,
        StopsMoving      = 214
    }
}
