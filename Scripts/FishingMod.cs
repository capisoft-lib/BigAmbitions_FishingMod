using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;

[assembly: RegisterModClass(typeof(FishingMod.FishingModEntry))]
[assembly: System.Reflection.AssemblyVersion("0.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.0.0")]
[assembly: InternalsVisibleTo("FishingMod.Editor")]

namespace FishingMod
{
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
                _host.AddComponent<FishingRuntime>().Initialize(message => context.Logger.Info(message));
            }
            catch
            {
                UnityEngine.Object.Destroy(_host);
                _host = null;
                throw;
            }

            context.Logger.Info("FishingMod 0.2.0 ready. Click outdoor water to cast, hook a fish and reel it in.");
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
