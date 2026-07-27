namespace Wonderfold.Core.Rules
{
    /// <summary>
    /// Every tunable number the core reads, in one place. Levels may override any of it, the Level
    /// Laboratory sweeps over it, and no rule constant is allowed to hide inside a service.
    /// </summary>
    public sealed class GameRules
    {
        public static GameRules Default => new GameRules();

        // ---- matching
        public int MinMatchLength = 3;

        /// <summary>Refills avoid colours that would instantly match at the spawn cell.</summary>
        public bool AvoidInstantMatchSpawns = true;

        // ---- booster creation thresholds
        public int RocketMatchLength = 4;
        public int PrismMatchLength = 5;
        public int BirdMatchLength = 6;

        /// <summary>
        /// How long a seam-crossing match must be to mint a Golden Stitch. The design intent is that
        /// <i>any</i> seam match is special; a strip fold puts a seam under a lot of ordinary threes, so
        /// this is exposed for the Level Laboratory to tune against measured win rates.
        /// </summary>
        public int SeamStitchMinLength = 4;

        // ---- booster power
        public int InkBloomRadius = 1;          // 3x3
        public int InkBloomComboRadius = 2;     // 5x5
        public int RocketComboBandHalfWidth = 1;// rocket+bloom clears a 3-wide cross
        public int BirdTargetCount = 1;
        public int BirdComboTargetCount = 3;

        /// <summary>A rocket that reaches a Story Seam continues this far onto the hidden face.</summary>
        public int SeamWrapDistance = 3;

        // ---- fold meter
        public int FoldMeterMax = 100;
        public int FoldChargePerTile = 6;
        public int FoldChargePerObstacleHit = 10;
        public int FoldChargePerSeamMatch = 25;

        // ---- safety valves (the core must never be able to hang the app)
        public int MaxCascadeDepth = 64;
        public int MaxGravityPasses = 512;
        public int MaxShuffleAttempts = 128;
        public int MaxBoosterChainDepth = 32;

        // ---- level flow
        /// <summary>Moves granted back when all goals complete, spent on the celebratory booster rain.</summary>
        public bool ConvertLeftoverMovesToBoosters = true;
        public int MaxLeftoverMoveBoosters = 8;

        public GameRules Clone() => (GameRules)MemberwiseClone();
    }
}
