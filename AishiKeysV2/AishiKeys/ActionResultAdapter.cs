using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AishiKeys
{
    internal static class ActionResultAdapter
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static IEnumerable<object> EnumerateActions(object result)
        {
            object collection = GetMemberValue(result, "Actions");
            if (!(collection is IEnumerable enumerable))
                yield break;

            foreach (object action in enumerable)
            {
                if (action != null)
                    yield return action;
            }
        }

        internal static bool TryAddAction(
            object result,
            string name,
            bool disabled,
            Action callback)
        {
            object collection = GetMemberValue(result, "Actions");
            if (collection == null)
                return false;

            Type actionType = ResolveCollectionElementType(collection);
            if (actionType == null)
                return false;

            object action;
            try
            {
                action = Activator.CreateInstance(actionType, true);
            }
            catch
            {
                return false;
            }

            if (action == null ||
                !TrySetMemberValue(action, "Name", name) ||
                !TrySetMemberValue(action, "Disabled", disabled) ||
                !TrySetMemberValue(action, "Action", callback))
            {
                return false;
            }

            return TryAddToCollection(collection, action);
        }

        internal static string GetName(object action)
        {
            return GetMemberValue(action, "Name") as string;
        }

        internal static bool TrySetAction(object action, Action callback)
        {
            return TrySetMemberValue(action, "Action", callback);
        }

        private static object GetMemberValue(object instance, string name)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, InstanceFlags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(name, InstanceFlags);
            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool TrySetMemberValue(object instance, string name, object value)
        {
            if (instance == null)
                return false;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, InstanceFlags);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                object converted;
                if (TryConvertValue(value, property.PropertyType, out converted))
                {
                    try
                    {
                        property.SetValue(instance, converted, null);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            FieldInfo field = type.GetField(name, InstanceFlags);
            if (field != null)
            {
                object converted;
                if (TryConvertValue(value, field.FieldType, out converted))
                {
                    try
                    {
                        field.SetValue(instance, converted);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static bool TryConvertValue(object value, Type targetType, out object converted)
        {
            converted = null;

            if (targetType == null)
                return false;

            if (value == null)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    return true;

                return false;
            }

            if (targetType.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            if (value is Delegate sourceDelegate && typeof(Delegate).IsAssignableFrom(targetType))
            {
                try
                {
                    converted = sourceDelegate.Target == null
                        ? Delegate.CreateDelegate(targetType, sourceDelegate.Method)
                        : Delegate.CreateDelegate(targetType, sourceDelegate.Target, sourceDelegate.Method);
                    return converted != null;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static Type ResolveCollectionElementType(object collection)
        {
            Type collectionType = collection.GetType();
            IEnumerable<Type> types = new[] { collectionType }
                .Concat(collectionType.GetInterfaces());

            foreach (Type type in types)
            {
                if (!type.IsGenericType)
                    continue;

                Type definition = type.GetGenericTypeDefinition();
                if (definition == typeof(ICollection<>) ||
                    definition == typeof(IList<>) ||
                    definition == typeof(IEnumerable<>))
                {
                    Type elementType = type.GetGenericArguments()[0];
                    if (elementType != typeof(object))
                        return elementType;
                }
            }

            if (collection is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (item != null)
                        return item.GetType();
                }
            }

            return null;
        }

        private static bool TryAddToCollection(object collection, object value)
        {
            if (collection is IList list)
            {
                try
                {
                    list.Add(value);
                    return true;
                }
                catch
                {
                }
            }

            MethodInfo addMethod = collection.GetType()
                .GetMethods(InstanceFlags)
                .Where(method => string.Equals(method.Name, "Add", StringComparison.Ordinal))
                .Where(method => method.GetParameters().Length == 1)
                .FirstOrDefault(method =>
                    method.GetParameters()[0].ParameterType.IsInstanceOfType(value));

            if (addMethod == null)
                return false;

            try
            {
                addMethod.Invoke(collection, new[] { value });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
