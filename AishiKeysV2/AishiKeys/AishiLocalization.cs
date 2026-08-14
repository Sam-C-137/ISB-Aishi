using System;
using System.Linq;
using System.Reflection;
using EFT;

namespace AishiKeys
{
    internal static class AishiLocalization
    {
        private const string LocalizationExtensionsTypeName = "EFT.LocalizationExtensions";

        private static readonly Lazy<MethodInfo> LocalizedMethod =
            new Lazy<MethodInfo>(ResolveLocalizedMethod);

        internal static string Localize(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key ?? string.Empty;

            MethodInfo method = LocalizedMethod.Value;
            if (method == null)
                return key;

            try
            {
                ParameterInfo[] parameters = method.GetParameters();
                object[] args = new object[parameters.Length];
                args[0] = key;

                for (int i = 1; i < parameters.Length; i++)
                {
                    if (parameters[i].HasDefaultValue)
                    {
                        args[i] = parameters[i].DefaultValue;
                    }
                    else if (!parameters[i].ParameterType.IsValueType ||
                             Nullable.GetUnderlyingType(parameters[i].ParameterType) != null)
                    {
                        args[i] = null;
                    }
                    else
                    {
                        args[i] = Activator.CreateInstance(parameters[i].ParameterType);
                    }
                }

                return method.Invoke(null, args) as string ?? key;
            }
            catch
            {
                return key;
            }
        }

        private static MethodInfo ResolveLocalizedMethod()
        {
            Type type = typeof(Player).Assembly.GetType(
                LocalizationExtensionsTypeName,
                false);

            if (type == null)
                return null;

            return type
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => string.Equals(method.Name, "Localized", StringComparison.Ordinal))
                .Where(method => method.ReturnType == typeof(string))
                .Where(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length >= 1 &&
                           parameters[0].ParameterType == typeof(string) &&
                           parameters.Skip(1).All(parameter => !parameter.ParameterType.IsByRef);
                })
                .OrderBy(method => method.GetParameters().Length)
                .ThenBy(method => method.MetadataToken)
                .FirstOrDefault();
        }
    }
}
