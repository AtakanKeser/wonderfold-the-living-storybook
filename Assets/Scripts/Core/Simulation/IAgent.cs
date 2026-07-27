using Wonderfold.Core.Board;
using Wonderfold.Core.Level;

namespace Wonderfold.Core.Simulation
{
    /// <summary>
    /// A bot that plays a level. The point of having several is that a level tuned only against a
    /// perfect player is not tuned at all: the interesting number is the gap between what a careless
    /// player clears and what an attentive one does.
    /// </summary>
    public interface IAgent
    {
        string Name { get; }

        /// <summary>Null means "I have nothing to play", which the simulator treats as a forfeit.</summary>
        PlayerMove? ChooseMove(LevelSession session);
    }
}
