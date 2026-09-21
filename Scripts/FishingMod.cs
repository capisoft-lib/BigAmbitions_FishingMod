using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BAModAPI;
using HarmonyLib;
using UnityEngine;

[assembly: RegisterModClass(typeof(FishingMod.FishingModInitializationEntry))]
[assembly: RegisterModClass(typeof(FishingMod.FishingModEntry))]
[assembly: System.Reflection.AssemblyVersion("1.0.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.1.0")]
[assembly: InternalsVisibleTo("FishingMod.Editor")]

namespace FishingMod
{
    [ModEntryOnInitializationLoad]
    public sealed class FishingModInitializationEntry : IModBigAmbitions
    {
        private const string HarmonyId = "capisoft.fishingmod.happiness-bootstrap";
        private Harmony _harmony;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _harmony = new Harmony(HarmonyId);
            try
            {
                FishingOptions.Register(context.ModId);
                FishingHappinessBootstrapPatch.Install(
                    _harmony,
                    message => context.Logger.Info(message));
                bool registeredImmediately = FishingHappinessService.TryRegisterDefinitions();
                context.Logger.Info(registeredImmediately
                    ? "FishingMod 1.0.1 happiness bootstrap installed; the existing native registry was updated."
                    : "FishingMod 1.0.1 happiness bootstrap armed for the native registry load.");
            }
            catch
            {
                FishingOptions.Unregister();
                _harmony.UnpatchAll(_harmony.Id);
                _harmony = null;
                throw;
            }
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            FishingOptions.Unregister();
            // Definitions can still be referenced by the active save. Keep them alive
            // until the game's subsystem reset clears the native registry.
            if (_harmony != null)
            {
                FishingHappinessBootstrapPatch.Uninstall(_harmony);
                _harmony = null;
            }
            return Task.CompletedTask;
        }
    }

    [ModEntryOnCityLoad]
    public sealed class FishingModEntry : IModBigAmbitions
    {
        private GameObject _host;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            if (UnityEngine.Object.FindObjectOfType<FishingRuntime>() != null)
                throw new InvalidOperationException("FishingMod is already loaded.");

            _host = new GameObject("FishingMod_Runtime");
            try
            {
                _host.AddComponent<FishingRuntime>().Initialize(context.ModRootPath, message => context.Logger.Info(message));
            }
            catch
            {
                UnityEngine.Object.Destroy(_host);
                _host = null;
                throw;
            }

            context.Logger.Info("FishingMod 1.0.1 ready. Click outdoor water to cast, hook a fish and reel it in.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_host != null)
            {
                _host.GetComponent<FishingRuntime>()?.Dispose();
                UnityEngine.Object.Destroy(_host);
                _host = null;
            }

            return Task.CompletedTask;
        }
    }
}
