namespace NexusForever.Game.Persistence
{
    /// <summary>
    /// Serialises normal player save requests and transfers ownership to the shutdown save path.
    /// </summary>
    internal sealed class PlayerSaveSerialiser
    {
        private PlayerSavePass activeSavePass;
        private bool followUpRequested;
        private bool takeover;

        /// <summary>
        /// Request a normal save, coalescing it when another save or shutdown takeover is active.
        /// </summary>
        /// <param name="startSave">Factory that starts a new save pass.</param>
        /// <returns>The new pass, or <see langword="null"/> when the request was coalesced.</returns>
        public PlayerSavePass Request(Func<PlayerSavePass> startSave)
        {
            ArgumentNullException.ThrowIfNull(startSave);

            if (takeover || activeSavePass != null)
            {
                followUpRequested = true;
                return null;
            }

            activeSavePass = startSave.Invoke();
            return activeSavePass;
        }

        /// <summary>
        /// Complete an owned normal pass and start at most one coalesced follow-up pass.
        /// </summary>
        public PlayerSaveTransition Complete(PlayerSavePass completedPass, Func<PlayerSavePass> startSave)
        {
            ArgumentNullException.ThrowIfNull(completedPass);
            ArgumentNullException.ThrowIfNull(startSave);

            if (takeover || !ReferenceEquals(activeSavePass, completedPass))
                return new PlayerSaveTransition();

            if (followUpRequested)
            {
                followUpRequested = false;
                activeSavePass = startSave.Invoke();
                return new PlayerSaveTransition
                {
                    IsHandled = true,
                    FollowUp  = activeSavePass
                };
            }

            activeSavePass = null;
            return new PlayerSaveTransition
            {
                IsHandled = true
            };
        }

        /// <summary>
        /// Transfer ownership to shutdown and return the normal pass already in flight, if any.
        /// </summary>
        public PlayerSavePass BeginTakeover()
        {
            takeover = true;
            followUpRequested = false;
            return activeSavePass;
        }

        /// <summary>
        /// Start the final pass owned by shutdown after any previous normal pass has settled.
        /// </summary>
        public PlayerSavePass StartTakeoverPass(Func<PlayerSavePass> startSave)
        {
            ArgumentNullException.ThrowIfNull(startSave);
            if (!takeover)
                throw new InvalidOperationException("A shutdown save takeover is not active.");

            activeSavePass = startSave.Invoke();
            return activeSavePass;
        }

        /// <summary>
        /// Release shutdown ownership and discard normal requests made after world processing stopped.
        /// </summary>
        public void EndTakeover(PlayerSavePass completedPass)
        {
            if (ReferenceEquals(activeSavePass, completedPass))
                activeSavePass = null;

            takeover = false;
            followUpRequested = false;
        }
    }

    /// <summary>
    /// Describes the result of completing a normal player save pass.
    /// </summary>
    internal sealed class PlayerSaveTransition
    {
        public bool IsHandled { get; init; }
        public PlayerSavePass FollowUp { get; init; }
    }
}
