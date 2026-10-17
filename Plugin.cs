using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Reactor;
using Reactor.Networking.Attributes;
using UnityEngine;

namespace SemLimites
{
    // ModFlags.None = só o anfitrião precisa ter o mod
    [BepInPlugin("dev.semlimites.among", "SemLimites", "1.0.0")]
    [BepInProcess("Among Us.exe")]
    [BepInDependency(ReactorPlugin.Id)]
    [ReactorModFlags(Reactor.Networking.ModFlags.None)]
    public class SemLimitesPlugin : BasePlugin
    {
        public static ConfigEntry<float> MaxValue;
        public static ConfigEntry<int> MaxAssignedTasks;

        public override void Load()
        {
            MaxValue = Config.Bind("Geral", "ValorMaximo", 9999f,
                "Valor máximo dos sliders (cooldowns, tempos e tarefas).");
            MaxAssignedTasks = Config.Bind("Tarefas", "MaximoTarefasPorJogador", 100,
                "Limite real de tarefas enviadas por jogador. Valores muito altos (centenas/milhares) estouram o limite de pacote da rede e podem derrubar a sala.");
            new Harmony("dev.semlimites.among").PatchAll();
        }
    }

    internal static class Unlock
    {
        static readonly string[] Keys = { "Cooldown", "Duration", "Task", "Time" };

        public static void Apply(NumberOption o)
        {
            if (o == null) return;
            var name = o.Title.ToString();
            foreach (var k in Keys)
            {
                if (name.Contains(k))
                {
                    o.ValidRange = new FloatRange(0f, SemLimitesPlugin.MaxValue.Value);
                    return;
                }
            }
        }

        // Shift = x10 | Ctrl = x100 | Shift+Ctrl = x1000
        public static float Mult()
        {
            bool s = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool c = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            return s && c ? 1000f : c ? 100f : s ? 10f : 1f;
        }
    }

    [HarmonyPatch(typeof(NumberOption), "SetUpFromData")]
    internal static class SetUpPatch
    {
        public static void Postfix(NumberOption __instance) => Unlock.Apply(__instance);
    }

    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Initialize))]
    internal static class InitPatch
    {
        public static void Prefix(NumberOption __instance) => Unlock.Apply(__instance);
    }

    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Increase))]
    internal static class IncPatch
    {
        public static void Prefix(NumberOption __instance, out float __state)
        {
            Unlock.Apply(__instance);
            __state = __instance.Increment;
            __instance.Increment *= Unlock.Mult();
        }
        public static void Postfix(NumberOption __instance, float __state) => __instance.Increment = __state;
    }

    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Decrease))]
    internal static class DecPatch
    {
        public static void Prefix(NumberOption __instance, out float __state)
        {
            Unlock.Apply(__instance);
            __state = __instance.Increment;
            __instance.Increment *= Unlock.Mult();
        }
        public static void Postfix(NumberOption __instance, float __state) => __instance.Increment = __state;
    }

    // Experimental: o jogo normalmente limita as tarefas ao que o mapa tem.
    // Aqui, se o total pedido for maior, repetimos as tarefas do jogador até chegar no total.
    [HarmonyPatch(typeof(GameData), nameof(GameData.RpcSetTasks))]
    internal static class MoreTasksPatch
    {
        public static void Prefix(ref Il2CppStructArray<byte> taskTypeIds)
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            var o = GameOptionsManager.Instance.CurrentGameOptions;
            int want = o.GetInt(Int32OptionNames.NumCommonTasks)
                     + o.GetInt(Int32OptionNames.NumLongTasks)
                     + o.GetInt(Int32OptionNames.NumShortTasks);
            want = Math.Min(want, SemLimitesPlugin.MaxAssignedTasks.Value);
            int have = taskTypeIds.Length;
            if (have == 0 || want <= have) return;

            var arr = new Il2CppStructArray<byte>(want);
            for (int i = 0; i < want; i++) arr[i] = taskTypeIds[i % have];
            taskTypeIds = arr;
        }
    }
}
