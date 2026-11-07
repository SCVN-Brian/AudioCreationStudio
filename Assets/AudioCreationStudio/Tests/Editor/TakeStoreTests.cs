using System;
using System.IO;
using AudioCreationStudio.EditorUI;
using AudioCreationStudio.Export;
using AudioCreationStudio.Models;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static AudioCreationStudio.Tests.TestUtil;

namespace AudioCreationStudio.Tests
{
    /// <summary>Folder operations behind the ElevenLabs take library (real asset folders, GUIDs and sidecars preserved).</summary>
    public class TakeStoreTests
    {
        private const string Root = "Assets/AudioCreationStudio/Tests/_TempTakes";

        [SetUp]
        public void SetUp() => TakeStore.EnsureFolder(Root);

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }

        private static string MakeTake(string folder, string name, string prompt = "dog bark")
        {
            TakeStore.EnsureFolder(folder);
            string path = $"{folder}/{name}.wav";
            File.WriteAllBytes(path, WavEncoder.Encode(Sine(440, 2400), WavSampleFormat.Pcm16));
            File.WriteAllText(TakeStore.SidecarPath(path), $"ElevenLabs sound effect\nprompt: {prompt}\n");
            AssetDatabase.ImportAsset(path);
            AssetDatabase.ImportAsset(TakeStore.SidecarPath(path));
            return path;
        }

        [Test]
        public void PathHelpers()
        {
            Assert.IsTrue(TakeStore.IsInside("A/B/c.wav", "A/B"));
            Assert.IsTrue(TakeStore.IsInside("A/B", "A/B/"));
            Assert.IsFalse(TakeStore.IsInside("A/Bc/x.wav", "A/B"), "prefix of a sibling name is not inside");
            Assert.AreEqual("A/B", TakeStore.Parent("A/B/c.wav"));
            Assert.AreEqual("Dogs  ›  Puppies", TakeStore.DisplayPath("R/Dogs/Puppies", "R", "Root"));
            Assert.AreEqual("Root", TakeStore.DisplayPath("R", "R", "Root"));
            Assert.AreEqual("Dogs", TakeStore.Sanitize("  Dogs/:  ", "x"));
            Assert.AreEqual("x", TakeStore.Sanitize(" / ", "x"));
            Assert.IsTrue(TakeStore.IsAudioFile("a/b.WAV"));
            Assert.IsFalse(TakeStore.IsAudioFile("a/b.txt"));
        }

        [Test]
        public void CreateFolder_IsUniqueAndListed()
        {
            string a = TakeStore.CreateFolder(Root, "Dogs");
            string b = TakeStore.CreateFolder(Root, "Dogs");
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(AssetDatabase.IsValidFolder(a) && AssetDatabase.IsValidFolder(b));
            CollectionAssert.AreEquivalent(new[] { a, b }, TakeStore.SubFolders(Root));
        }

        [Test]
        public void MoveTake_KeepsGuidAndSidecar()
        {
            string take = MakeTake(Root, "bark");
            string guid = AssetDatabase.AssetPathToGUID(take);
            string dogs = TakeStore.CreateFolder(Root, "Dogs");

            string moved = TakeStore.MoveTake(take, dogs);

            Assert.AreEqual($"{dogs}/bark.wav", moved);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(moved), "GUID survives so timeline references stay valid");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AudioClip>(moved));
            Assert.IsFalse(File.Exists(take));
            Assert.AreEqual("dog bark", TakeStore.ReadPrompt(moved), "prompt sidecar moved with the take");
            Assert.AreEqual(0, TakeStore.CountTakes(Root, false));
            Assert.AreEqual(1, TakeStore.CountTakes(Root, true));
        }

        [Test]
        public void MoveTake_NameClashGetsUniqueName()
        {
            string dogs = TakeStore.CreateFolder(Root, "Dogs");
            MakeTake(dogs, "bark", "first");
            string other = MakeTake(Root, "bark", "second");

            string moved = TakeStore.MoveTake(other, dogs);

            Assert.AreNotEqual($"{dogs}/bark.wav", moved);
            Assert.AreEqual(2, TakeStore.Takes(dogs, false).Count);
            Assert.AreEqual("second", TakeStore.ReadPrompt(moved));
        }

        [Test]
        public void MoveFolder_NestsAndRejectsSelf()
        {
            string dogs = TakeStore.CreateFolder(Root, "Dogs");
            string animals = TakeStore.CreateFolder(Root, "Animals");
            MakeTake(dogs, "bark");

            string moved = TakeStore.MoveFolder(dogs, animals);

            Assert.AreEqual($"{animals}/Dogs", moved);
            Assert.AreEqual(1, TakeStore.CountTakes(moved, false));
            Assert.Throws<InvalidOperationException>(() => TakeStore.MoveFolder(animals, moved), "cannot move a folder into its own child");
            Assert.Throws<InvalidOperationException>(() => TakeStore.MoveFolder(animals, animals));
        }

        [Test]
        public void Rename_TakeAndFolder()
        {
            string dogs = TakeStore.CreateFolder(Root, "Dogs");
            string take = MakeTake(dogs, "bark");

            string renamedTake = TakeStore.Rename(take, "Big Bark");
            Assert.AreEqual($"{dogs}/Big Bark.wav", renamedTake);
            Assert.IsTrue(File.Exists(TakeStore.SidecarPath(renamedTake)));

            string renamedFolder = TakeStore.Rename(dogs, "Canines");
            Assert.AreEqual($"{Root}/Canines", renamedFolder);
            Assert.IsTrue(File.Exists($"{renamedFolder}/Big Bark.wav"));
            Assert.Throws<InvalidOperationException>(() => TakeStore.Rename(renamedFolder, "  "));
        }

        [Test]
        public void DeleteFolder_CanKeepTakes()
        {
            string dogs = TakeStore.CreateFolder(Root, "Dogs");
            string pups = TakeStore.CreateFolder(dogs, "Puppies");
            MakeTake(dogs, "a");
            MakeTake(pups, "b");

            int moved = TakeStore.DeleteFolder(dogs, keepTakes: true);

            Assert.AreEqual(2, moved);
            Assert.IsFalse(AssetDatabase.IsValidFolder(dogs));
            Assert.AreEqual(2, TakeStore.CountTakes(Root, false), "takes moved up to the parent");

            string cats = TakeStore.CreateFolder(Root, "Cats");
            MakeTake(cats, "meow");
            TakeStore.DeleteFolder(cats, keepTakes: false);
            Assert.AreEqual(2, TakeStore.CountTakes(Root, true));
        }
    }
}
