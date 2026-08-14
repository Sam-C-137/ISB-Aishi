using Comfort.Common;
using EFT;
using EFT.Interactive;
using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AishiKeys
{
    public static class WorldInteractionUtils
    {
        private static readonly string[] StableIdMemberNames =
        {
            "Id",
            "InteractiveId",
            "ObjectId",
            "DoorId",
            "NetId"
        };

        public static bool IsBotInteraction(GamePlayerOwner owner)
        {
            int? ownerId = null;
            if (owner != null)
            {
                Player player = owner.Player;
                ownerId = player != null ? new int?(player.Id) : null;
            }

            int? mainPlayerId = null;
            GameWorld instance = Singleton<GameWorld>.Instance;
            if (instance != null)
            {
                Player mainPlayer = instance.MainPlayer;
                mainPlayerId = mainPlayer != null ? new int?(mainPlayer.Id) : null;
            }

            return !(ownerId.GetValueOrDefault() == mainPlayerId.GetValueOrDefault() &&
                     (ownerId != null) == (mainPlayerId != null));
        }

        internal static string GetStableInteractiveId(WorldInteractiveObject interactive)
        {
            if (interactive == null)
                return string.Empty;

            Type type = interactive.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (string memberName in StableIdMemberNames)
            {
                PropertyInfo property = FindProperty(type, memberName, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        object value = property.GetValue(interactive, null);
                        string text = ConvertStableValue(value);
                        if (!string.IsNullOrEmpty(text))
                            return memberName + ":" + text;
                    }
                    catch
                    {
                    }
                }

                FieldInfo field = FindField(type, memberName, flags);
                if (field != null)
                {
                    try
                    {
                        object value = field.GetValue(interactive);
                        string text = ConvertStableValue(value);
                        if (!string.IsNullOrEmpty(text))
                            return memberName + ":" + text;
                    }
                    catch
                    {
                    }
                }
            }

            Transform transform = interactive.transform;
            if (transform == null)
                return string.Empty;

            Scene scene = interactive.gameObject.scene;
            Vector3 position = transform.position;

            return "Path:" +
                   (scene.IsValid() ? scene.name : string.Empty) + "|" +
                   BuildHierarchyPath(transform) + "|" +
                   position.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," +
                   position.y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," +
                   position.z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static WorldInteractiveObject FindInteractiveByStableId(string stableId)
        {
            if (string.IsNullOrEmpty(stableId))
                return null;

            WorldInteractiveObject[] interactives = Resources.FindObjectsOfTypeAll<WorldInteractiveObject>();
            if (interactives == null)
                return null;

            foreach (WorldInteractiveObject interactive in interactives)
            {
                if (interactive == null || interactive.gameObject == null)
                    continue;

                Scene scene = interactive.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                if (string.Equals(
                        GetStableInteractiveId(interactive),
                        stableId,
                        StringComparison.Ordinal))
                {
                    return interactive;
                }
            }

            return null;
        }

        internal static bool TryApplyAuthoritativeDoorState(
            WorldInteractiveObject door,
            int authorityState)
        {
            if (door == null)
                return false;

            EDoorState targetState = ResolveFallbackDoorState(authorityState);

            if (door.DoorState == targetState)
                return true;

            if (door.DoorState == EDoorState.Locked && targetState != EDoorState.Locked)
            {
                try
                {
                    door.Interact(EInteractionType.Unlock);
                }
                catch
                {
                }

                if (door.DoorState != EDoorState.Locked &&
                    (authorityState < 0 || door.DoorState == targetState))
                {
                    return true;
                }
            }

            Type type = door.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo property = FindProperty(type, "DoorState", flags);
            if (property != null && property.PropertyType == typeof(EDoorState))
            {
                MethodInfo setter = property.GetSetMethod(true);
                if (setter != null)
                {
                    try
                    {
                        setter.Invoke(door, new object[] { targetState });
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo[] fields = current.GetFields(flags | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    if (field.FieldType != typeof(EDoorState))
                        continue;

                    if (field.Name.IndexOf("doorstate", StringComparison.OrdinalIgnoreCase) < 0 &&
                        field.Name.IndexOf("doorState", StringComparison.OrdinalIgnoreCase) < 0 &&
                        field.Name.IndexOf("<DoorState>", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    try
                    {
                        field.SetValue(door, targetState);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static EDoorState ResolveFallbackDoorState(int authorityState)
        {
            
            
            if (authorityState >= byte.MinValue &&
                authorityState <= byte.MaxValue &&
                authorityState != (int)EDoorState.Locked)
            {
                return (EDoorState)(byte)authorityState;
            }

            string[] preferredNames = { "Shut", "Closed", "Open" };
            foreach (string name in preferredNames)
            {
                EDoorState parsed;
                if (Enum.TryParse(name, true, out parsed) && (int)parsed != (int)EDoorState.Locked)
                    return parsed;
            }

            Array values = Enum.GetValues(typeof(EDoorState));
            foreach (object value in values)
            {
                EDoorState state = (EDoorState)value;
                if ((int)state != (int)EDoorState.Locked)
                    return state;
            }

            return (EDoorState)0;
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            StringBuilder builder = new StringBuilder();
            Transform current = transform;

            while (current != null)
            {
                if (builder.Length > 0)
                    builder.Insert(0, '/');

                builder.Insert(0, current.name ?? string.Empty);
                current = current.parent;
            }

            return builder.ToString();
        }

        private static string ConvertStableValue(object value)
        {
            if (value == null)
                return string.Empty;

            string text = value as string;
            if (text != null)
                return text.Trim();

            if (value is Guid)
                return value.ToString();

            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is decimal)
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

            return string.Empty;
        }

        private static PropertyInfo FindProperty(Type type, string name, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property;
            }

            return null;
        }

        private static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }

            return null;
        }
    }
}
