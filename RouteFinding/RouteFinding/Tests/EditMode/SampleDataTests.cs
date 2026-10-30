using System.Collections.Generic;
using CampusNav.Routing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CampusNav.Routing.Tests
{
    /// <summary>
    /// Checks the JSON files in the project load through Unity's JsonUtility (the same path the app uses)
    /// and have no warnings. Add your real campus file's name to <see cref="Files"/>.
    /// </summary>
    public class SampleDataTests
    {
        static readonly string[] Files = { "campus_graph.sample" };

        [Test]
        public void JsonFilesLoadWithoutWarnings([ValueSource(nameof(Files))] string fileName)
        {
            string[] guids = AssetDatabase.FindAssets(fileName + " t:TextAsset");
            if (guids.Length == 0) Assert.Ignore($"{fileName}.json not found in the project.");
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));

            var warnings = new List<string>();
            var graph = NavGraphBuilder.Build(JsonUtility.FromJson<NavGraphData>(asset.text), warnings);
            Assert.Greater(graph.Nodes.Count, 0);
            CollectionAssert.IsEmpty(warnings, string.Join("\n", warnings));
        }

        [Test]
        public void JsonUtilityKeepsSampleStructure()
        {
            string[] guids = AssetDatabase.FindAssets("campus_graph.sample t:TextAsset");
            if (guids.Length == 0) Assert.Ignore("Sample not imported.");
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
            var fromJson = NavGraphBuilder.Build(JsonUtility.FromJson<NavGraphData>(asset.text));
            var fromCode = TestGraphs.Campus();
            Assert.AreEqual(fromCode.Nodes.Count, fromJson.Nodes.Count);
            Assert.AreEqual(fromCode.Edges.Count, fromJson.Edges.Count);
            var a = new RouteService(fromJson).FindRoute("GATE", "ROBOTICS_201");
            var b = new RouteService(fromCode).FindRoute("GATE", "ROBOTICS_201");
            Assert.AreEqual(b.Cost, a.Cost, 1e-3f);
        }
    }
}
