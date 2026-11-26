using System.Reflection;
using System.Reflection.Emit;
using AutoDuty.Logging;
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value

namespace AutoDuty.Helpers
{
    using System;
    using System.IO;
    using System.Linq;
    using ECommons.DalamudServices;
    using ECommons.EzSharedDataManager;
    using ECommons.Reflection;
    using static Data.Enums;

    internal static class ReflectionHelper
    {
        // What do you mean just (BindingFlags) 60 isn't great ?
        public const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        internal static class Avarice_Reflection
        {
            private static readonly bool avariceReady;

            public static bool PositionalChanged(out Positional positional)
            {
                if (avariceReady && Configuration is { DutyConfig: { AutoManageBossModAISettings: true, BossMod.PositionalRoleBased: true, BossMod.PositionalAvarice: true } })
                {
                    positional = Positional.Any;

                    if (EzSharedData.TryGet<uint[]>("Avarice.PositionalStatus", out uint[] ret))
                    {
                        if (ret[1] == 1)
                            positional = Positional.Rear;
                        if (ret[1] == 2)
                            positional = Positional.Flank;
                    }

                    if (Configuration.DutyConfig.BossMod.PositionalEnum != positional)
                    {
                        Configuration.DutyConfig.BossMod.PositionalEnum = positional;
                        return true;
                    }
                }
                positional = Configuration.DutyConfig.BossMod.PositionalEnum;
                return false;
            }


            static Avarice_Reflection()
            {
                if (DalamudReflector.TryGetDalamudPlugin("Avarice", out _, false, true)) 
                    avariceReady = true;
            }
        }


        public delegate ref F FieldRef<F>();
        internal static FieldRef<F> StaticFieldRefAccess<F>(FieldInfo fieldInfo)
        {
            if (!fieldInfo.IsStatic)
                throw new ArgumentException("Field must be static");

            DynamicMethod dm = new($"__refget_{fieldInfo.DeclaringType?.Name ?? "null"}_static_fi_{fieldInfo.Name}", typeof(F).MakeByRefType(), []);

            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldsflda, fieldInfo);
            il.Emit(OpCodes.Ret);

            return (FieldRef<F>)dm.CreateDelegate(typeof(FieldRef<F>));
        }

        public delegate ref F FieldRef<in T, F>(T instance);

        internal static FieldRef<T, F> FieldRefAccess<T, F>(FieldInfo fieldInfo, bool needCastClass)
        {
            Type  delegateInstanceType = typeof(T);
            Type? declaringType        = fieldInfo.DeclaringType;

            DynamicMethod dm = new($"__refget_{delegateInstanceType.Name}_fi_{fieldInfo.Name}", typeof(F).MakeByRefType(), [delegateInstanceType]);

            ILGenerator il = dm.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            if (needCastClass && declaringType != null)
                il.Emit(OpCodes.Castclass, declaringType);
            il.Emit(OpCodes.Ldflda, fieldInfo);

            il.Emit(OpCodes.Ret);

            return dm.CreateDelegate<FieldRef<T, F>>();
        }

        public delegate bool StaticBoolMethod();

        public delegate bool InstanceBoolMethod<in T>(T instance);

        public delegate string InstanceStringMethod<in T>(T instance);

        public delegate FileInfo InstanceFileInfoMethod<in T>(T instance);


        public static DelegateType MethodDelegate<DelegateType>(MethodInfo method, object? instance = null, Type? delegateInstanceType = null, Type[]? delegateArgs = null, bool debug = false) where DelegateType : Delegate
        {
            try
            {
                ArgumentNullException.ThrowIfNull(method);
                Type delegateType = typeof(DelegateType);

                if (method.IsStatic)
                    return (DelegateType)Delegate.CreateDelegate(delegateType, method);

                Type? declaringType = method.DeclaringType;

                if (instance is null)
                {
                    ParameterInfo[] delegateParameters = delegateType.GetMethod("Invoke")!.GetParameters();
                    delegateInstanceType ??= delegateParameters[0].ParameterType;

                    if (declaringType is { IsInterface: true } && delegateInstanceType.IsValueType)
                    {
                        InterfaceMapping interfaceMapping = delegateInstanceType.GetInterfaceMap(declaringType);
                        method        = interfaceMapping.TargetMethods[Array.IndexOf(interfaceMapping.InterfaceMethods, method)];
                        declaringType = delegateInstanceType;
                    }
                }

                ParameterInfo[] parameters     = method.GetParameters();
                int             numParameters  = parameters.Length;
                Type[]          parameterTypes = new Type[numParameters + 1];
                parameterTypes[0] = declaringType!;
                for (int i = 0; i < numParameters; i++)
                    parameterTypes[i + 1] = parameters[i].ParameterType;

                Type[]        delegateArgsResolved = delegateArgs ?? delegateType.GetGenericArguments();
                Type[]        dynMethodReturn      = delegateArgsResolved.Length < parameterTypes.Length ? parameterTypes : delegateArgsResolved;
                DynamicMethod dmd                  = new("OpenInstanceDelegate_" + method.Name, method.ReturnType, dynMethodReturn);
                ILGenerator   ilGen                = dmd.GetILGenerator();
                if (declaringType is { IsValueType: true } && delegateArgsResolved.Length > 0 && !delegateArgsResolved[0].IsByRef)
                    ilGen.Emit(OpCodes.Ldarga_S, 0);
                else
                    ilGen.Emit(OpCodes.Ldarg_0);
                for (int i = 1; i < parameterTypes.Length; i++)
                {
                    ilGen.Emit(OpCodes.Ldarg, i);

                    if (parameterTypes[i].IsValueType && i < delegateArgsResolved.Length &&
                        !delegateArgsResolved[i].IsValueType)

                        ilGen.Emit(OpCodes.Unbox_Any, parameterTypes[i]);
                }

                ilGen.Emit(OpCodes.Call, method);
                ilGen.Emit(OpCodes.Ret);

                if (debug)
                {
                    ADLogging.Warning(delegateType.FullName                ?? string.Empty);
                    ADLogging.Warning(delegateType.ReflectedType?.FullName ?? string.Empty);
                    ADLogging.Warning(dmd.Name + " " + dmd.ReturnType.FullName + " " + string.Join(" | ", dmd.GetParameters().Select(p => p.ParameterType.FullName)));
                    ADLogging.Warning(string.Join(" | ", delegateArgsResolved.Select(t => t.FullName)));
                    ADLogging.Warning(string.Join(" | ", parameterTypes.Select(t => t.FullName)));
                }

                return (DelegateType)dmd.CreateDelegate(delegateType);
            }
            catch (Exception ex)
            {
                ADLogging.Error(ex.ToString());
            }

            return null!;
        }
    }
}
