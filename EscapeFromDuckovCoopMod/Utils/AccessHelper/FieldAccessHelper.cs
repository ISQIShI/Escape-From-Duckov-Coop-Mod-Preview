using System;
using System.Reflection;

namespace EscapeFromDuckovCoopMod.Utils.AccessHelper
{
    public static partial class AccessHelper
    {
        /// <summary>
        /// 链式字段访问器，封装字段反射与常用操作。
        /// </summary>
        public readonly struct FieldChain<TDeclaring>
        {
            private readonly Type _declaringType;
            private readonly string _fieldName;

            internal FieldChain(Type declaringType, string fieldName)
            {
                if (declaringType == null)
                {
                    throw new ArgumentNullException(nameof(declaringType));
                }

                _declaringType = declaringType;
                _fieldName = NormalizeMemberName(fieldName);
            }

            /// <summary>
            /// 声明字段的类型。
            /// </summary>
            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            /// <summary>
            /// 字段名称。
            /// </summary>
            public string FieldName
            {
                get { return _fieldName; }
            }

            /// <summary>
            /// 直接获取字段元数据对象。
            /// </summary>
            public FieldInfo Info
            {
                get { return Instance.GetFieldInfo(_declaringType, _fieldName); }
            }

            /// <summary>
            /// 读取实例字段的值。
            /// </summary>
            public TField GetValue<TField>(TDeclaring instance)
            {
                if (ReferenceEquals(instance, null))
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                return Instance.GetFieldValue<TDeclaring,TField>(instance, _fieldName);
            }

            /// <summary>
            /// 设置实例字段的值。
            /// </summary>
            public void SetValue<TField>(TDeclaring instance, TField value)
            {
                if (ReferenceEquals(instance, null))
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                Instance.SetFieldValue(instance, _fieldName, value);
            }

            /// <summary>
            /// 获取对应的 FieldRef 委托。
            /// </summary>
            public AccessTools.FieldRef<TDeclaring, TField> GetFieldRef<TField>()
            {
                return Instance.GetFieldRef<TDeclaring, TField>(_fieldName);
            }
        }
        
        public readonly struct FieldCacheKey : IEquatable<FieldCacheKey>
        {
            private readonly Type _declaringType;
            private readonly string _fieldName;
            private readonly int _hashCode;

            public FieldCacheKey(Type declaringType, string fieldName)
            {
                _declaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
                _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
                _hashCode = CalculateHashCode(_declaringType, _fieldName);
            }

            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            public string FieldName
            {
                get { return _fieldName; }
            }

            public bool Equals(FieldCacheKey other)
            {
                return _declaringType == other._declaringType && string.Equals(_fieldName, other._fieldName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is FieldCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return _hashCode;
            }

            public void Deconstruct(out Type declaringType, out string fieldName)
            {
                declaringType = _declaringType;
                fieldName = _fieldName;
            }

            private static int CalculateHashCode(Type declaringType, string fieldName)
            {
                unchecked
                {
                    var hash = declaringType.GetHashCode();
                    hash = (hash * 397) ^ fieldName.GetHashCode(StringComparison.Ordinal);
                    return hash;
                }
            }
        }

        private sealed class FieldCacheEntry
        {
            private readonly object _syncRoot = new object();
            private FieldInfo _fieldInfo;
            private Delegate _delegate;
            private bool? _isStatic;

            public FieldInfo GetOrCreateInfo(FieldCacheKey cacheKey, bool expectStatic, out bool created)
            {
                created = false;
                if (_fieldInfo != null)
                {
                    EnsureFieldMatchesExpectation(_fieldInfo, cacheKey, expectStatic);
                    return _fieldInfo;
                }

                lock (_syncRoot)
                {
                    if (_fieldInfo != null)
                    {
                        EnsureFieldMatchesExpectation(_fieldInfo, cacheKey, expectStatic);
                        return _fieldInfo;
                    }

                    var (declaringType, fieldName) = cacheKey;
                    var fieldInfo = AccessTools.Field(declaringType, fieldName);
                    if (fieldInfo == null)
                    {
                        throw new FieldNotFoundException(declaringType, fieldName);
                    }
                    _fieldInfo = fieldInfo;
                    _isStatic = fieldInfo.IsStatic;

                    EnsureFieldMatchesExpectation(fieldInfo, cacheKey, expectStatic);

                    created = true;
                    return fieldInfo;
                }
            }

            public AccessTools.FieldRef<TInstance, TField> GetOrCreateInstanceFieldRef<TInstance, TField>(FieldCacheKey cacheKey, out bool created)
            {
                created = false;
                var fieldInfo = GetOrCreateInfo(cacheKey, false, out _);

                if (_delegate is AccessTools.FieldRef<TInstance, TField> typedRef)
                {
                    return typedRef;
                }

                if (_delegate != null && _delegate is not AccessTools.FieldRef<TInstance, TField>)
                {
                    throw new AccessHelperException($"字段 {cacheKey.FieldName} 已缓存不同签名的委托，无法转换为 {typeof(AccessTools.FieldRef<TInstance, TField>).FullName}。");
                }

                lock (_syncRoot)
                {
                    if (_delegate is AccessTools.FieldRef<TInstance, TField> cached)
                    {
                        return cached;
                    }
                    
                    var fieldRef = AccessTools.FieldRefAccess<TInstance, TField>(fieldInfo);
                    _delegate = fieldRef;
                    created = true;
                    return fieldRef;
                }
            }

            public AccessTools.FieldRef<TField> GetOrCreateStaticFieldRef<TField>(FieldCacheKey cacheKey, out bool created)
            {
                created = false;
                var fieldInfo = GetOrCreateInfo(cacheKey, true, out _);

                if (_delegate is AccessTools.FieldRef<TField> typedRef)
                {
                    return typedRef;
                }

                if (_delegate != null && _delegate is not AccessTools.FieldRef<TField>)
                {
                    throw new AccessHelperException($"字段 {cacheKey.FieldName} 已缓存不同签名的委托，无法转换为 {typeof(AccessTools.FieldRef<TField>).FullName}。");
                }

                lock (_syncRoot)
                {
                    if (_delegate is AccessTools.FieldRef<TField> cached)
                    {
                        return cached;
                    }

                    var fieldRef = AccessTools.StaticFieldRefAccess<TField>(fieldInfo);
                    _delegate = fieldRef;
                    created = true;
                    return fieldRef;
                }
            }

            private void EnsureFieldMatchesExpectation(FieldInfo fieldInfo, FieldCacheKey cacheKey, bool expectStatic)
            {
                var actualStatic = _isStatic ?? fieldInfo.IsStatic;
                if (actualStatic != expectStatic)
                {
                    var message = expectStatic
                        ? $"字段 {cacheKey.FieldName} 并非静态字段，无法通过静态成员访问。"
                        : $"字段 {cacheKey.FieldName} 是静态字段，无法通过实例成员访问。";
                    throw new AccessHelperException(message);
                }
            }
        }
    }
}