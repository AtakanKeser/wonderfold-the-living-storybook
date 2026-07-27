using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Board
{
    /// <summary>
    /// A character standing on the board — Quill padding towards a torn corner of the page, say.
    ///
    /// <para>Walkers live on the board rather than in a level script because they are physically part of
    /// the puzzle: they occupy a coordinate, they are printed on one face of the paper, and folding that
    /// face away leaves them stranded until you turn it back.</para>
    /// </summary>
    public sealed class Walker
    {
        public int Id { get; }
        public GridCoord Position { get; internal set; }
        public GridCoord Target { get; }
        public SurfaceSide Side { get; }
        public int StepsPerTurn { get; }
        public string CharacterId { get; }
        public bool HasArrived { get; internal set; }

        public Walker(int id, GridCoord position, GridCoord target, SurfaceSide side, int stepsPerTurn = 2,
            string characterId = "quill")
        {
            Id = id;
            Position = position;
            Target = target;
            Side = side;
            StepsPerTurn = stepsPerTurn < 1 ? 1 : stepsPerTurn;
            CharacterId = characterId;
            HasArrived = position == target;
        }

        public Walker Clone() => new Walker(Id, Position, Target, Side, StepsPerTurn, CharacterId)
        {
            HasArrived = HasArrived
        };

        public override string ToString() => $"{CharacterId}#{Id} {Position}->{Target}{(HasArrived ? " ✔" : "")}";
    }
}
