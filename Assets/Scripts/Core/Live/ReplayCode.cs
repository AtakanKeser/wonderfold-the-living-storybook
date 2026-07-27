using System;
using System.Collections.Generic;
using System.Text;
using Wonderfold.Core.Board;
using Wonderfold.Core.Level;
using Wonderfold.Core.Primitives;

namespace Wonderfold.Core.Live
{
    /// <summary>A whole playthrough, small enough to paste into a message.</summary>
    public struct ReplayThread
    {
        /// <summary>Which page this was played on: <c>daily-207</c>, <c>endless-42</c>, <c>page-10</c>.</summary>
        public string PageKey;

        /// <summary>The seed the session was built with. Together with the key this fixes the board.</summary>
        public int Seed;

        public List<PlayerMove> Moves;

        public ReplayThread(string pageKey, int seed, List<PlayerMove> moves)
        {
            PageKey = pageKey;
            Seed = seed;
            Moves = moves ?? new List<PlayerMove>();
        }
    }

    /// <summary>
    /// Compresses a run into a short shareable string — a <b>Story Thread</b> — and reads it back.
    ///
    /// <para>The core has one property that no other match-3 codebase can casually claim: a level is
    /// fully determined by a seed, and a playthrough is fully determined by that seed plus the ordered
    /// list of player intents. Nothing about the board is inferred from rendering, and no
    /// <c>System.Random</c> is involved, so the same code replays identically on any device and any
    /// runtime version. That means a solution can be <i>sent</i> to another player and re-played on their
    /// phone, move for move, with no server, no video and no trust required — and the receiving client
    /// can recompute the claimed score rather than believe it.</para>
    ///
    /// <para>The encoding is bit-packed, so a typical thirty-move run is about seventy characters: a swap
    /// costs fourteen bits because the second cell is stored as a direction rather than a coordinate, and
    /// a fold costs six. The alphabet is Crockford base32 — no I, L, O or U — so a code read aloud or
    /// retyped survives the obvious confusions, and a checksum means a mangled code is rejected instead
    /// of replayed into nonsense.</para>
    /// </summary>
    public static class ReplayCode
    {
        private const int Version = 1;
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        private const int KindDaily = 0;
        private const int KindEndless = 1;
        private const int KindAuthored = 2;
        private const int KindRaw = 3;

        /// <summary>Coordinates are stored in four bits, so the format supports boards up to 16 wide.</summary>
        public const int MaxCoordinate = 15;
        public const int MaxMoves = 4095;

        public static bool TryEncode(ReplayThread thread, out string code, out string error)
        {
            code = null;
            error = null;

            var writer = new BitWriter();
            writer.Write(Version, 4);
            if (!WriteKey(writer, thread.PageKey, out error)) return false;
            writer.Write(unchecked((uint)thread.Seed), 32);

            var moves = thread.Moves ?? new List<PlayerMove>();
            if (moves.Count > MaxMoves)
            {
                error = $"A thread cannot hold more than {MaxMoves} moves.";
                return false;
            }

            writer.Write((uint)moves.Count, 12);
            for (int i = 0; i < moves.Count; i++)
            {
                if (!WriteMove(writer, moves[i], out error)) return false;
            }

            var payload = writer.ToBytes();
            var full = new byte[payload.Length + 2];
            Array.Copy(payload, full, payload.Length);
            ushort checksum = Checksum(payload);
            full[payload.Length] = (byte)(checksum >> 8);
            full[payload.Length + 1] = (byte)(checksum & 0xFF);

            code = ToBase32(full);
            return true;
        }

        public static string Encode(ReplayThread thread)
        {
            if (!TryEncode(thread, out string code, out string error)) throw new InvalidOperationException(error);
            return code;
        }

        public static bool TryDecode(string code, out ReplayThread thread, out string error)
        {
            thread = default;
            error = null;

            if (!TryFromBase32(code, out byte[] full, out error)) return false;
            if (full.Length < 8)
            {
                error = "That thread is too short to be a Wonderfold code.";
                return false;
            }

            var payload = new byte[full.Length - 2];
            Array.Copy(full, payload, payload.Length);
            ushort expected = (ushort)((full[full.Length - 2] << 8) | full[full.Length - 1]);
            if (Checksum(payload) != expected)
            {
                error = "That thread is damaged — check for a missing or swapped character.";
                return false;
            }

            var reader = new BitReader(payload);
            int version = (int)reader.Read(4);
            if (version != Version)
            {
                error = $"That thread was written by a different version of the game (v{version}).";
                return false;
            }

            if (!ReadKey(reader, out string key, out error)) return false;
            int seed = unchecked((int)reader.Read(32));
            int count = (int)reader.Read(12);

            var moves = new List<PlayerMove>(count);
            for (int i = 0; i < count; i++)
            {
                if (!ReadMove(reader, out var move, out error)) return false;
                moves.Add(move);
            }

            thread = new ReplayThread(key, seed, moves);
            return true;
        }

        // ------------------------------------------------------------------ page key

        private static bool WriteKey(BitWriter writer, string key, out string error)
        {
            error = null;

            if (DailyFold.TryParseKey(key, out int day) && day >= 0 && day < (1 << 20))
            {
                writer.Write(KindDaily, 2);
                writer.Write((uint)day, 20);
                return true;
            }

            if (EndlessArchive.TryParseKey(key, out int depth) && depth >= 0 && depth < (1 << 20))
            {
                writer.Write(KindEndless, 2);
                writer.Write((uint)depth, 20);
                return true;
            }

            if (TryParseAuthored(key, out int id) && id >= 0 && id < (1 << 12))
            {
                writer.Write(KindAuthored, 2);
                writer.Write((uint)id, 12);
                return true;
            }

            // Anything else travels as text, so a future page family costs a format bump rather than a
            // broken code.
            if (string.IsNullOrEmpty(key) || key.Length > 32)
            {
                error = "That page cannot be written into a thread.";
                return false;
            }

            writer.Write(KindRaw, 2);
            writer.Write((uint)key.Length, 6);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (c > 127)
                {
                    error = "Page keys must be plain ASCII.";
                    return false;
                }

                writer.Write((uint)c, 7);
            }

            return true;
        }

        private static bool ReadKey(BitReader reader, out string key, out string error)
        {
            error = null;
            key = null;

            int kind = (int)reader.Read(2);
            switch (kind)
            {
                case KindDaily:
                    key = DailyFold.Key((int)reader.Read(20));
                    return true;
                case KindEndless:
                    key = EndlessArchive.Key((int)reader.Read(20));
                    return true;
                case KindAuthored:
                    key = AuthoredKey((int)reader.Read(12));
                    return true;
                default:
                {
                    int length = (int)reader.Read(6);
                    var builder = new StringBuilder(length);
                    for (int i = 0; i < length; i++) builder.Append((char)reader.Read(7));
                    key = builder.ToString();
                    return true;
                }
            }
        }

        public static string AuthoredKey(int levelId) =>
            "page-" + levelId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static bool TryParseAuthored(string key, out int levelId)
        {
            levelId = 0;
            if (key == null || !key.StartsWith("page-", StringComparison.Ordinal)) return false;
            return int.TryParse(key.Substring(5), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out levelId);
        }

        // ------------------------------------------------------------------ moves

        private static bool WriteMove(BitWriter writer, PlayerMove move, out string error)
        {
            error = null;
            writer.Write((uint)(int)move.Kind, 2);

            switch (move.Kind)
            {
                case MoveKind.Swap:
                    if (!InRange(move.A) || !InRange(move.B))
                    {
                        error = "A thread only supports boards up to 16 cells wide.";
                        return false;
                    }

                    if (!move.A.IsOrthogonalNeighbourOf(move.B))
                    {
                        error = "A swap in a thread must be between neighbouring cells.";
                        return false;
                    }

                    writer.Write((uint)move.A.X, 4);
                    writer.Write((uint)move.A.Y, 4);
                    // The partner cell is always a neighbour, so two bits of direction beat eight bits
                    // of coordinate.
                    writer.Write((uint)(int)DirectionExtensions.FromDelta(move.B.X - move.A.X, move.B.Y - move.A.Y), 2);
                    return true;

                case MoveKind.ActivateBooster:
                    if (!InRange(move.A))
                    {
                        error = "A thread only supports boards up to 16 cells wide.";
                        return false;
                    }

                    writer.Write((uint)move.A.X, 4);
                    writer.Write((uint)move.A.Y, 4);
                    return true;

                case MoveKind.Fold:
                    if (move.RegionId < 0 || move.RegionId > 15)
                    {
                        error = "A thread supports at most 16 fold regions per page.";
                        return false;
                    }

                    writer.Write((uint)move.RegionId, 4);
                    return true;

                case MoveKind.UseTool:
                    if (!InRange(move.A))
                    {
                        error = "A thread only supports boards up to 16 cells wide.";
                        return false;
                    }

                    writer.Write((uint)(int)move.Tool, 2);
                    writer.Write((uint)move.A.X, 4);
                    writer.Write((uint)move.A.Y, 4);
                    return true;

                default:
                    error = "Unknown move kind.";
                    return false;
            }
        }

        private static bool ReadMove(BitReader reader, out PlayerMove move, out string error)
        {
            error = null;
            move = default;
            var kind = (MoveKind)reader.Read(2);

            switch (kind)
            {
                case MoveKind.Swap:
                {
                    int x = (int)reader.Read(4);
                    int y = (int)reader.Read(4);
                    var direction = (Direction)reader.Read(2);
                    var a = new GridCoord(x, y);
                    move = PlayerMove.Swap(a, a.Step(direction));
                    return true;
                }

                case MoveKind.ActivateBooster:
                    move = PlayerMove.ActivateBooster(new GridCoord((int)reader.Read(4), (int)reader.Read(4)));
                    return true;

                case MoveKind.Fold:
                    move = PlayerMove.Fold((int)reader.Read(4));
                    return true;

                case MoveKind.UseTool:
                {
                    var tool = (PageTool)reader.Read(2);
                    int x = (int)reader.Read(4);
                    int y = (int)reader.Read(4);
                    move = PlayerMove.UseTool(tool, new GridCoord(x, y));
                    return true;
                }

                default:
                    error = "Unknown move kind in thread.";
                    return false;
            }
        }

        private static bool InRange(GridCoord coord) =>
            coord.X >= 0 && coord.Y >= 0 && coord.X <= MaxCoordinate && coord.Y <= MaxCoordinate;

        // ------------------------------------------------------------------ base32 + checksum

        private static string ToBase32(byte[] data)
        {
            var builder = new StringBuilder(data.Length * 8 / 5 + 8);
            int buffer = 0;
            int bits = 0;

            for (int i = 0; i < data.Length; i++)
            {
                buffer = (buffer << 8) | data[i];
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    builder.Append(Alphabet[(buffer >> bits) & 31]);
                }
            }

            if (bits > 0) builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);

            // Grouped for the eye; the reader throws separators away again.
            var grouped = new StringBuilder(builder.Length + builder.Length / 5);
            for (int i = 0; i < builder.Length; i++)
            {
                if (i > 0 && i % 5 == 0) grouped.Append('-');
                grouped.Append(builder[i]);
            }

            return grouped.ToString();
        }

        private static bool TryFromBase32(string code, out byte[] data, out string error)
        {
            data = null;
            error = null;
            if (string.IsNullOrEmpty(code))
            {
                error = "Paste a Story Thread code first.";
                return false;
            }

            var bytes = new List<byte>(code.Length);
            int buffer = 0;
            int bits = 0;

            for (int i = 0; i < code.Length; i++)
            {
                char c = char.ToUpperInvariant(code[i]);
                if (c == '-' || c == ' ' || c == '_' || c == '\n' || c == '\r' || c == '\t') continue;

                // Crockford's forgiving letters: these are the characters people actually mistype.
                if (c == 'I' || c == 'L') c = '1';
                else if (c == 'O') c = '0';
                else if (c == 'U') c = 'V';

                int value = Alphabet.IndexOf(c);
                if (value < 0)
                {
                    error = $"'{code[i]}' is not part of a Story Thread code.";
                    return false;
                }

                buffer = (buffer << 5) | value;
                bits += 5;
                if (bits < 8) continue;
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }

            data = bytes.ToArray();
            return true;
        }

        private static ushort Checksum(byte[] data)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < data.Length; i++)
                {
                    hash ^= data[i];
                    hash *= 16777619u;
                }

                return (ushort)((hash ^ (hash >> 16)) & 0xFFFF);
            }
        }

        private sealed class BitWriter
        {
            private readonly List<byte> _bytes = new List<byte>(64);
            private int _current;
            private int _bits;

            public void Write(int value, int width) => Write((uint)value, width);

            public void Write(uint value, int width)
            {
                for (int i = width - 1; i >= 0; i--)
                {
                    _current = (_current << 1) | (int)((value >> i) & 1u);
                    _bits++;
                    if (_bits != 8) continue;
                    _bytes.Add((byte)_current);
                    _current = 0;
                    _bits = 0;
                }
            }

            public byte[] ToBytes()
            {
                if (_bits == 0) return _bytes.ToArray();
                var copy = new List<byte>(_bytes) { (byte)(_current << (8 - _bits)) };
                return copy.ToArray();
            }
        }

        private sealed class BitReader
        {
            private readonly byte[] _data;
            private int _position;

            public BitReader(byte[] data)
            {
                _data = data;
            }

            public uint Read(int width)
            {
                uint value = 0;
                for (int i = 0; i < width; i++)
                {
                    int index = _position >> 3;
                    int bit = index < _data.Length ? (_data[index] >> (7 - (_position & 7))) & 1 : 0;
                    value = (value << 1) | (uint)bit;
                    _position++;
                }

                return value;
            }
        }
    }

    /// <summary>
    /// Plays a shared thread back against the page it claims to have been played on.
    ///
    /// <para>A leaderboard without a server is normally a leaderboard you cannot believe. Here the claim
    /// carries its own proof: the thread contains the moves, the page is reproducible from its key, and
    /// replaying one against the other yields the score. A doctored code does not produce a bigger
    /// number, it produces a different — usually losing — run.</para>
    /// </summary>
    public static class ReplayVerifier
    {
        public static bool TryReplay(LevelDefinition definition, ReplayThread thread, out LevelRunStats stats,
            out string error)
        {
            stats = null;
            error = null;

            if (definition == null)
            {
                error = "That page could not be found.";
                return false;
            }

            var session = new LevelSession(definition, thread.Seed);
            session.Events.Recording = false;
            session.Start();

            var moves = thread.Moves ?? new List<PlayerMove>();
            for (int i = 0; i < moves.Count; i++)
            {
                if (session.IsOver) break;
                if (session.TryExecute(moves[i], out string reason)) continue;

                error = $"Move {i + 1} does not fit this page ({reason}).";
                return false;
            }

            stats = session.Stats;
            if (stats.Outcome == LevelOutcome.InProgress)
            {
                stats.Outcome = session.AllGoalsComplete() ? LevelOutcome.Won : LevelOutcome.InProgress;
                stats.MovesRemaining = session.MovesRemaining;
                stats.MovesUsed = definition.Moves - session.MovesRemaining;
            }

            return true;
        }

        /// <summary>The score a shared thread actually earns, recomputed rather than taken on trust.</summary>
        public static int Verify(LevelDefinition definition, ReplayThread thread, out string error)
        {
            if (!TryReplay(definition, thread, out var stats, out error)) return 0;
            return RunScore.StoryInk(stats);
        }
    }
}
