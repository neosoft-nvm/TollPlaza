using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;

namespace TollPlaza
{
    /// <summary>
    /// Works out where a road's car lanes sit across its width, from the road prefab's own building
    /// blocks (sections laid left to right, each made of pieces, each piece listing its lanes). The plaza's
    /// islands and booths are then placed exactly on the lane lines of the highway that runs through it.
    /// Only unconditional sections/pieces count (ones without upgrade requirements), which is the plain
    /// road as placed.
    /// </summary>
    internal static class RoadLaneProbe
    {
        /// <summary>Car lane centers, left to right, relative to the road's center line; and the road width.</summary>
        public static bool TryGetCarLanes(NetGeometryPrefab road, out List<float> laneCenters, out float width)
        {
            laneCenters = new List<float>();
            width = 0f;
            if (road == null || road.m_Sections == null)
            {
                return false;
            }

            foreach (NetSectionInfo info in road.m_Sections)
            {
                if (info.m_Section == null || HasRequirements(info.m_RequireAll, info.m_RequireAny))
                {
                    continue;
                }
                AddSection(info.m_Section, info.m_Invert, ref width, laneCenters);
            }

            float half = width * 0.5f;
            laneCenters = laneCenters.Select(x => x - half).OrderBy(x => x).ToList();
            return laneCenters.Count > 0 && width > 0f;
        }

        private static void AddSection(NetSectionPrefab section, bool invert, ref float cursor, List<float> lanes)
        {
            // A section either has its own pieces or is built from sub-sections laid side by side.
            if (section.m_SubSections != null && section.m_SubSections.Length > 0)
            {
                foreach (NetSubSectionInfo sub in section.m_SubSections)
                {
                    if (sub.m_Section != null && !HasRequirements(sub.m_RequireAll, sub.m_RequireAny))
                    {
                        AddSection(sub.m_Section, invert, ref cursor, lanes);
                    }
                }
                return;
            }

            float sectionWidth = 0f;
            var sectionLanes = new List<float>();
            foreach (NetPieceInfo pieceInfo in section.m_Pieces ?? new NetPieceInfo[0])
            {
                NetPiecePrefab piece = pieceInfo.m_Piece;
                if (piece == null)
                {
                    continue;
                }
                // Alternative pieces (flat, elevated, gravel...) share the section's width, and some sections
                // (medians) only have conditional pieces, so every piece counts toward the width.
                if (piece.m_Layer == NetPieceLayer.Surface || piece.m_Layer == NetPieceLayer.Bottom)
                {
                    sectionWidth = System.Math.Max(sectionWidth, piece.m_Width);
                }
                if (HasRequirements(pieceInfo.m_RequireAll, pieceInfo.m_RequireAny))
                {
                    continue;
                }
                NetPieceLanes pieceLanes = piece.GetComponent<NetPieceLanes>();
                if (pieceLanes?.m_Lanes == null)
                {
                    continue;
                }
                foreach (NetLaneInfo lane in pieceLanes.m_Lanes)
                {
                    if (lane.m_Lane != null && lane.m_Lane.GetComponent<CarLane>() != null)
                    {
                        sectionLanes.Add(pieceInfo.m_Offset.x + lane.m_Position.x);
                    }
                }
            }

            float center = cursor + sectionWidth * 0.5f;
            foreach (float x in sectionLanes)
            {
                lanes.Add(center + (invert ? -x : x));
            }
            cursor += sectionWidth;
        }

        /// <summary>Width of one section as laid in a road.</summary>
        public static float SectionWidth(NetSectionPrefab section)
        {
            float width = 0f;
            AddSection(section, false, ref width, new List<float>());
            return width;
        }

        /// <summary>True when the section carries a car lane (a driving lane, not a shoulder or median).</summary>
        public static bool HasCarLane(NetSectionPrefab section)
        {
            var lanes = new List<float>();
            float width = 0f;
            AddSection(section, false, ref width, lanes);
            return lanes.Count > 0;
        }

        private static bool HasRequirements(NetPieceRequirements[] all, NetPieceRequirements[] any) =>
            (all != null && all.Length > 0) || (any != null && any.Length > 0);
    }
}
