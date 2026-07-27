namespace Wonderfold.Core.Level
{
    /// <summary>
    /// Something that acts on the board between turns rather than in response to a match: The Blank
    /// draining colour, ink creeping across tiles, Quill walking his path, a paper dragon re-armouring.
    ///
    /// <para>Modifiers are where a level's personality lives. They can do anything to the board, but only
    /// at turn boundaries, which keeps the cascade itself a pure function of the player's move.</para>
    /// </summary>
    public interface ILevelModifier
    {
        string Id { get; }
        void Initialise(LevelSession session);
        void OnTurnStarted(LevelSession session);
        void OnTurnEnded(LevelSession session);
    }

    public abstract class LevelModifier : ILevelModifier
    {
        protected LevelModifier(string id)
        {
            Id = id;
        }

        public string Id { get; }
        public virtual void Initialise(LevelSession session) { }
        public virtual void OnTurnStarted(LevelSession session) { }
        public virtual void OnTurnEnded(LevelSession session) { }
    }
}
