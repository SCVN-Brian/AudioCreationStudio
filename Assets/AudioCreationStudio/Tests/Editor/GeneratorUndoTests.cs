using System.Reflection;
using AudioCreationStudio.DSP;
using AudioCreationStudio.EditorUI;
using AudioCreationStudio.Models;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AudioCreationStudio.Tests
{
    public class GeneratorUndoTests
    {
        private StudioState _state;
        private StudioSession _session;
        private GeneratorView _view;

        [SetUp]
        public void SetUp()
        {
            _state = new StudioState { generator = GeneratorPresets.CoinPickup() };
            _session = new StudioSession(_state, false);
            var ctx = new StudioContext { Session = _session, Library = SourceLibrary.Shared, Waveforms = WaveformCache.Shared };
            ctx.Commands = new StudioCommands(ctx);
            _view = new GeneratorView(ctx, _state);
        }

        [TearDown]
        public void TearDown()
        {
            _session.Dispose();
            if (_state.generatorDoc != null) { Undo.ClearUndo(_state.generatorDoc); Object.DestroyImmediate(_state.generatorDoc); }
            Undo.ClearUndo(_state.working);
            Object.DestroyImmediate(_state.working);
        }

        private void Invoke(string method, params object[] args) =>
            typeof(GeneratorView).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_view, args);

        [Test]
        public void StandaloneEdits_UndoAndRedo()
        {
            Assert.AreEqual("Coin Pickup", _view.CurrentSettings.presetName);
            _view.ApplyExternal(GeneratorPresets.Impact(), "Load Impact");
            Assert.AreEqual("Impact", _view.CurrentSettings.presetName);
            Undo.PerformUndo();
            Assert.AreEqual("Coin Pickup", _view.CurrentSettings.presetName, "Ctrl+Z restores the previous generator settings");
            Undo.PerformRedo();
            Assert.AreEqual("Impact", _view.CurrentSettings.presetName);
        }

        [Test]
        public void SliderDrag_CollapsesIntoOneUndoStep()
        {
            float before = _view.CurrentSettings.frequency;
            Invoke("BeginDrag", "Generator Frequency");
            for (int i = 1; i <= 10; i++)
            {
                float f = before + i * 50;
                Invoke("Edit", "Generator Frequency", (System.Action<GeneratorSettings>)(g => g.frequency = f), false);
            }
            Invoke("EndDrag");
            Assert.AreEqual(before + 500, _view.CurrentSettings.frequency, 1e-3f);
            Undo.PerformUndo();
            Assert.AreEqual(before, _view.CurrentSettings.frequency, 1e-3f, "one undo reverts the whole drag");
        }

        [Test]
        public void GeneratorUndo_DoesNotTouchRecipe()
        {
            int tracks = _session.Data.tracks.Count;
            _view.ApplyExternal(GeneratorPresets.Whoosh(), "Load Whoosh");
            Undo.PerformUndo();
            Assert.AreEqual(tracks, _session.Data.tracks.Count);
            Assert.IsFalse(_session.IsDirty);
        }
    }
}
