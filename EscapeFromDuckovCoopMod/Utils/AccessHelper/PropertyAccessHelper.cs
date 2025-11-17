using System;
using System.Reflection;

namespace EscapeFromDuckovCoopMod.Utils.AccessHelper
{
    public static partial class AccessHelper
    {
        /// <summary>
        /// 链式属性访问器，聚合常用的属性读写操作。
        /// </summary>
        public readonly struct PropertyChain<TDeclaring>
        {
            private readonly Type _declaringType;
            private readonly string _propertyName;

            internal PropertyChain(Type declaringType, string propertyName)
            {
                if (declaringType == null)
                {
                    throw new ArgumentNullException(nameof(declaringType));
                }

                _declaringType = declaringType;
                _propertyName = NormalizeMemberName(propertyName);
            }

            /// <summary>
            /// 声明属性的类型。
            /// </summary>
            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            /// <summary>
            /// 属性名称。
            /// </summary>
            public string PropertyName
            {
                get { return _propertyName; }
            }

            /// <summary>
            /// 直接获取属性元数据对象。
            /// </summary>
            public PropertyInfo Info
            {
                get { return Instance.GetPropertyInfo(_declaringType, _propertyName); }
            }

            /// <summary>
            /// 读取实例属性的值。
            /// </summary>
            public TProperty GetValue<TProperty>(TDeclaring instance)
            {
                if (ReferenceEquals(instance, null))
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                return Instance.GetPropertyValue<TProperty>(instance, _propertyName);
            }

            /// <summary>
            /// 设置实例属性的值。
            /// </summary>
            public void SetValue<TProperty>(TDeclaring instance, TProperty value)
            {
                if (ReferenceEquals(instance, null))
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                Instance.SetPropertyValue(instance, _propertyName, value);
            }
        }

        public readonly struct PropertyCacheKey : IEquatable<PropertyCacheKey>
        {
            private readonly Type _declaringType;
            private readonly string _propertyName;
            private readonly int _hashCode;

            public PropertyCacheKey(Type declaringType, string propertyName)
            {
                _declaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
                _propertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
                _hashCode = CalculateHashCode(_declaringType, _propertyName);
            }

            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            public string PropertyName
            {
                get { return _propertyName; }
            }

            public bool Equals(PropertyCacheKey other)
            {
                return _declaringType == other._declaringType && string.Equals(_propertyName, other._propertyName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is PropertyCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return _hashCode;
            }

            public void Deconstruct(out Type declaringType, out string propertyName)
            {
                declaringType = _declaringType;
                propertyName = _propertyName;
            }

            private static int CalculateHashCode(Type declaringType, string propertyName)
            {
                unchecked
                {
                    var hash = declaringType.GetHashCode();
                    hash = (hash * 397) ^ propertyName.GetHashCode(StringComparison.Ordinal);
                    return hash;
                }
            }
        }

        private sealed class PropertyCacheEntry
        {
            private readonly object _syncRoot = new object();
            private PropertyInfo _propertyInfo;
            private bool? _isStatic;

            public PropertyInfo GetOrCreateInfo(PropertyCacheKey cacheKey, bool? expectStatic, out bool created)
            {
                created = false;
                if (_propertyInfo != null)
                {
                    EnsurePropertyMatchesExpectation(_propertyInfo, expectStatic, cacheKey);
                    return _propertyInfo;
                }

                lock (_syncRoot)
                {
                    if (_propertyInfo != null)
                    {
                        EnsurePropertyMatchesExpectation(_propertyInfo, expectStatic, cacheKey);
                        return _propertyInfo;
                    }

                    var (declaringType, propertyName) = cacheKey;
                    var propertyInfo = AccessTools.Property(declaringType, propertyName);
                    if (propertyInfo == null)
                    {
                        throw new PropertyNotFoundException(declaringType, propertyName);
                    }

                    _isStatic = DetermineIsStatic(propertyInfo);
                    EnsurePropertyMatchesExpectation(propertyInfo, expectStatic, cacheKey);
                    _propertyInfo = propertyInfo;
                    created = true;
                    return propertyInfo;
                }
            }

            private void EnsurePropertyMatchesExpectation(PropertyInfo propertyInfo, bool? expectStatic, PropertyCacheKey cacheKey)
            {
                if (!expectStatic.HasValue)
                {
                    return;
                }

                var actualStatic = _isStatic ?? DetermineIsStatic(propertyInfo);
                if (actualStatic != expectStatic.Value)
                {
                    var message = expectStatic.Value
                        ? $"属性 {cacheKey.PropertyName} 并非静态属性，无法通过静态成员访问。"
                        : $"属性 {cacheKey.PropertyName} 是静态属性，无法通过实例成员访问。";
                    throw new AccessHelperException(message);
                }
            }

            private static bool DetermineIsStatic(PropertyInfo propertyInfo)
            {
                var getter = propertyInfo.GetGetMethod(true);
                if (getter != null && getter.IsStatic)
                {
                    return true;
                }

                var setter = propertyInfo.GetSetMethod(true);
                return setter != null && setter.IsStatic;
            }
        }
    }
}