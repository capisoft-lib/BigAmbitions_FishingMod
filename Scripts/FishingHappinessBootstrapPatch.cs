using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Helpers;

namespace FishingMod
{
    internal static class FishingHappinessBootstrapPatch
    {
        internal static readonly MethodInfo TargetMethod = AccessTools.Method(
            typeof(HappinessHelper),
            nameof(HappinessHelper.OnHappinessModifiersLoaded),
            new[] { typeof(IList<HappinessModifier>) });

        private static readonly MethodInfo PostfixMethod = AccessTools.Method(
            typeof(FishingHappinessBootstrapPatch),
            nameof(AfterNativeDefinitionsLoaded));

        private static Action<string> _log;

        internal static void Install(Harmony harmony, Action<string> log)
        {
            if (harmony == null) throw new ArgumentNullException(nameof(harmony));
            if (TargetMethod == null)
                throw new MissingMethodException(
                    typeof(HappinessHelper).FullName,
                    nameof(HappinessHelper.OnHappinessModifiersLoaded));
            if (PostfixMethod == null)
                throw new MissingMethodException(
                    typeof(FishingHappinessBootstrapPatch).FullName,
                    nameof(AfterNativeDefinitionsLoaded));

            _log = log;
            harmony.Patch(TargetMethod, postfix: new HarmonyMethod(PostfixMethod));
        }

        internal static void Uninstall(Harmony harmony)
        {
            if (harmony != null)
                harmony.UnpatchAll(harmony.Id);
            _log = null;
        }

        private static void AfterNativeDefinitionsLoaded()
        {
            FishingHappinessService.RegisterDefinitions();
            _log?.Invoke("FishingMod happiness definitions registered immediately after the native registry load.");
        }
    }
}
