using System.Diagnostics;
using System.Threading.Tasks;
using EFT.InventoryLogic;
using UnityEngine;

namespace QuickTraderLoadTimes
{
    /// <summary>
    /// Fix candidate 1 (off unless FastIconRender is on): render uncached item icons as fast as a
    /// per-frame time budget allows, instead of the game's fixed pacing.
    ///
    /// The game's capture step (IconCreatorBase.FillIconWithNewSpriteAsync's lambda) takes the
    /// one-camera lock, waits a JobScheduler frame, captures, waits another frame, then releases.
    /// Measured on 2026-10-01 (cold cache, 60 FPS lobby): one icon per ~2 frames, 30-35 icons/s,
    /// while each capture costs only ~4-5 ms. This version captures as soon as the lock is free
    /// and the frame still has budget left, so several icons can be captured in one frame.
    ///
    /// The capture itself (IconCreatorBase.CaptureSpriteOfModel) and everything around it (bundle
    /// loading, the prefab, the mip-0 wait, saving the PNG, the cache index) are the game's own,
    /// unchanged. Only ItemIconCreator is affected: the clothing and player icon creators, and
    /// 7Bpencil WeaponCamo's own path for decal items, keep the game's pacing.
    /// </summary>
    internal static class FastRender
    {
        private static int _frame = -1;
        private static double _spentThisFrame;

        public static int Captures;
        public static int FramesWithMultipleCaptures;
        private static int _capturesThisFrame;

        /// <summary>Prefix on IconCreatorBase&lt;Item, ItemIcon&gt;.CG_MoveNext.method_0.</summary>
        public static bool Prefix(IconCreatorBase<Item, ItemIcon>.CG_MoveNext __instance, GameObject model, PreviewPivot pivot, ref Task<Sprite> __result)
        {
            if (!QuickTraderLoadTimesPlugin.FastIconRender.Value || !Scope.Active) return true;
            if (!(__instance.IconCreatorBase is ItemIconCreator creator)) return true;
            __result = Capture(creator, model, __instance.size, pivot);
            return false;
        }

        private static async Task<Sprite> Capture(ItemIconCreator creator, GameObject model, IntVec2 size, PreviewPivot pivot)
        {
            await creator.PrepareCameraAsync();

            // Task.Yield resumes on Unity's synchronization context: once per frame, in Update.
            while (creator._isIconCreating || !MayCaptureThisFrame())
            {
                await Task.Yield();
            }

            creator._isIconCreating = true;
            long start = Stopwatch.GetTimestamp();
            try
            {
                return creator.CaptureSpriteOfModel(model, in size, pivot);
            }
            finally
            {
                creator._isIconCreating = false;
                Spend(TraderSession.Elapsed(start, Stopwatch.GetTimestamp()));
            }
        }

        /// <summary>The first capture in a frame always goes ahead, so this is never slower than one per frame.</summary>
        private static bool MayCaptureThisFrame()
        {
            int frame = Time.frameCount;
            if (frame != _frame)
            {
                _frame = frame;
                _spentThisFrame = 0;
                _capturesThisFrame = 0;
                return true;
            }
            return _spentThisFrame < QuickTraderLoadTimesPlugin.RenderBudgetMs.Value;
        }

        private static void Spend(double ms)
        {
            _spentThisFrame += ms;
            Captures++;
            _capturesThisFrame++;
            if (_capturesThisFrame == 2) FramesWithMultipleCaptures++;
        }
    }
}
