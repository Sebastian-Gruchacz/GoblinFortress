using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ObscureWare.ModernD20;

namespace ObscureWare.Common
{
    public static class CommonExtensions
    {
        public static bool IsDerrivedFrom(this Type @type, Type parentType)
        {
            if (parentType == typeof (object))
            {
                return true; // always ....
            }

            while (@type != typeof (object))
            {
                if (@type == parentType)
                {
                    return true;
                }
                @type = @type.BaseType;
            }

            return false;
        }

        public static bool Implements(this Type @type, Type @interface)
        {
            Debug.Assert(@interface.IsInterface);

            var interfaces = @type.FindInterfaces((type1, criteria) => type1 == (Type) criteria, @interface);
            return interfaces.Any();
        }


        public static void Shuffle<T>(this T[] @array, IRandomizer randomizer)
        {
            for (int i = 0; i < array.Length; i++)
            {
                int newPos = randomizer.NextInt(i, array.Length - 1);
                if (newPos != i)
                {
                    T tmp = @array[i];
                    @array[i] = @array[newPos];
                    @array[newPos] = tmp;
                }
            }
        }

        public static void Fill<T>(this T[] array, T defaultValue)
        {
            for (int i = 0; i < array.Length; ++i)
            {
                array[i] = defaultValue;
            }
        }
    }
}
