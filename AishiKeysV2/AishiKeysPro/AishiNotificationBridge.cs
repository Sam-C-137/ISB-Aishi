using System;
using System.Reflection;
using EFT;
using UnityEngine;

namespace AishiKeysPro
{
    internal static class AishiNotificationBridge
    {
        private static MethodInfo _displayMethod;
        private static bool _resolved;

        public static void Display(string message, float duration, int iconType, Color color)
        {
            MethodInfo method = ResolveDisplayMethod();
            if (method == null)
            {
                return;
            }

            try
            {
                ParameterInfo[] parameters = method.GetParameters();
                object[] arguments = new object[4];
                arguments[0] = message;
                arguments[1] = ConvertValue(duration, parameters[1].ParameterType);
                arguments[2] = ConvertValue(iconType, parameters[2].ParameterType);
                arguments[3] = ConvertColor(color, parameters[3].ParameterType);
                method.Invoke(null, arguments);
            }
            catch (Exception ex)
            {
                AishiKeysMod.Logger?.LogWarning("Aishi notification failed: " + ex.Message);
            }
        }

        private static MethodInfo ResolveDisplayMethod()
        {
            if (_resolved)
            {
                return _displayMethod;
            }

            _resolved = true;

            Type[] types;
            Assembly assembly = typeof(Player).Assembly;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            if (types == null)
            {
                return null;
            }

            foreach (Type type in types)
            {
                if (type == null)
                {
                    continue;
                }

                MethodInfo[] methods;

                try
                {
                    methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (method.Name != "DisplayMessageNotification")
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 4 || parameters[0].ParameterType != typeof(string))
                    {
                        continue;
                    }

                    _displayMethod = method;
                    return _displayMethod;
                }
            }

            AishiKeysMod.Logger?.LogWarning("Aishi notification API was not found in Assembly-CSharp.");
            return null;
        }

        private static object ConvertValue(object value, Type targetType)
        {
            Type nullableType = Nullable.GetUnderlyingType(targetType);
            Type effectiveType = nullableType ?? targetType;

            if (effectiveType.IsEnum)
            {
                return Enum.ToObject(effectiveType, value);
            }

            if (effectiveType.IsInstanceOfType(value))
            {
                return value;
            }

            return Convert.ChangeType(value, effectiveType);
        }

        private static object ConvertColor(Color color, Type targetType)
        {
            Type nullableType = Nullable.GetUnderlyingType(targetType);
            Type effectiveType = nullableType ?? targetType;

            if (effectiveType == typeof(Color) || effectiveType.IsAssignableFrom(typeof(Color)))
            {
                return color;
            }

            return null;
        }
    }
}
