using System;
using System.Reflection;

namespace EscapeFromDuckovCoopMod.Utils.AccessHelper
{
    public static partial class AccessHelper
    {
        /// <summary>
        /// 链式方法访问器，支持参数类型约束与调用。
        /// </summary>
        public readonly struct MethodChain<TDeclaring>
        {
            private readonly Type _declaringType;
            private readonly string _methodName;
            private readonly Type[] _parameterTypes;

            internal MethodChain(Type declaringType, string methodName, Type[] parameterTypes)
            {
                if (declaringType == null)
                {
                    throw new ArgumentNullException(nameof(declaringType));
                }

                _declaringType = declaringType;
                _methodName = NormalizeMemberName(methodName);

                if (parameterTypes == null)
                {
                    _parameterTypes = null;
                }
                else if (parameterTypes.Length == 0)
                {
                    _parameterTypes = Array.Empty<Type>();
                }
                else
                {
                    _parameterTypes = NormalizeParameterTypes(parameterTypes);
                }
            }

            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            public string MethodName
            {
                get { return _methodName; }
            }

            /// <summary>
            /// 指定方法的参数类型，生成新的方法访问器以便锁定重载。
            /// </summary>
            public MethodChain<TDeclaring> WithParameters(params Type[] parameterTypes)
            {
                return new MethodChain<TDeclaring>(_declaringType, _methodName, parameterTypes);
            }

            /// <summary>
            /// 获取方法元数据对象，若设置了参数类型则使用其确定重载。
            /// </summary>
            public MethodInfo Info
            {
                get { return Instance.GetMethodInfo(_declaringType, _methodName, _parameterTypes ?? Array.Empty<Type>()); }
            }

            /// <summary>
            /// 调用实例方法，不关心返回值。
            /// </summary>
            public void Invoke(TDeclaring instance, params object[] parameters)
            {
                Invoke<object>(instance, parameters);
            }

            /// <summary>
            /// 调用实例方法并返回指定类型的结果。
            /// </summary>
            public TReturn Invoke<TReturn>(TDeclaring instance, params object[] parameters)
            {
                if (ReferenceEquals(instance, null))
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                var args = parameters ?? Array.Empty<object>();

                if (_parameterTypes != null)
                {
                    var methodInfo = Instance.GetMethodInfo(_declaringType, _methodName, _parameterTypes);
                    var result = methodInfo.Invoke(instance, args);
                    return CastReturnValue<TReturn>(result, methodInfo);
                }

                return Instance.InvokeMethod<TReturn>(instance, _methodName, args);
            }
        }
        
        /// <summary>
        /// 方法缓存的字典键，包含声明类型、方法名及参数签名。
        /// </summary>
        public readonly struct MethodCacheKey : IEquatable<MethodCacheKey>
        {
            private readonly Type _declaringType;
            private readonly string _methodName;
            private readonly Type[] _parameterTypes;
            private readonly int _hashCode;

            public MethodCacheKey(Type declaringType, string methodName, Type[] parameterTypes)
            {
                _declaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
                _methodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
                _parameterTypes = parameterTypes?.Length > 0 ? (Type[])parameterTypes.Clone() : Array.Empty<Type>();
                _hashCode = CalculateHashCode(_declaringType, _methodName, _parameterTypes);
            }

            public bool Equals(MethodCacheKey other)
            {
                if (_declaringType != other._declaringType)
                {
                    return false;
                }

                if (!string.Equals(_methodName, other._methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                if (_parameterTypes.Length != other._parameterTypes.Length)
                {
                    return false;
                }

                for (var i = 0; i < _parameterTypes.Length; i++)
                {
                    if (_parameterTypes[i] != other._parameterTypes[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                return obj is MethodCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return _hashCode;
            }

            public Type DeclaringType => _declaringType;

            public string MethodName => _methodName;

            public Type[] ParameterTypes => _parameterTypes;
            
            /// <summary>
            /// 解构方法
            /// </summary>
            /// <param name="declaringType"></param>
            /// <param name="methodName"></param>
            /// <param name="parameterTypes"></param>
            public void Deconstruct(out Type declaringType, out string methodName, out Type[] parameterTypes)
            {
                declaringType = _declaringType;
                methodName = _methodName;
                parameterTypes = _parameterTypes;
            }

            private static int CalculateHashCode(Type declaringType, string methodName, Type[] parameterTypes)
            {
                unchecked
                {
                    var hash = declaringType.GetHashCode();
                    hash = (hash * 397) ^ methodName.GetHashCode(StringComparison.Ordinal);
                    foreach (var parameter in parameterTypes)
                    {
                        hash = (hash * 397) ^ (parameter?.GetHashCode() ?? 0);
                    }

                    return hash;
                }
            }
        }
        
        private sealed class MethodCacheEntry
        {
            private readonly object _syncRoot = new object();
            private MethodInfo _methodInfo;
            private bool? _isStatic;
            private Delegate _delegate;

            public MethodInfo GetOrCreateInfo(MethodCacheKey methodCacheKey, bool? expectStatic, out bool created)
            {
                created = false;
                if (_methodInfo != null)
                {
                    EnsureMethodMatchesExpectation(_methodInfo, expectStatic);
                    return _methodInfo;
                }

                lock (_syncRoot)
                {
                    if (_methodInfo != null)
                    {
                        EnsureMethodMatchesExpectation(_methodInfo, expectStatic);
                        return _methodInfo;
                    }

                    var (declaringType, methodName, parameterTypes) = methodCacheKey;

                    var parameters = parameterTypes != null && parameterTypes.Length > 0 ? parameterTypes : null;
                    var methodInfo = AccessTools.Method(declaringType, methodName, parameters);
                    if (methodInfo == null)
                    {
                        throw new MethodNotFoundException(declaringType, methodName, parameterTypes ?? Array.Empty<Type>());
                    }

                    _methodInfo = methodInfo;
                    _isStatic = methodInfo.IsStatic;
                    EnsureMethodMatchesExpectation(methodInfo, expectStatic);
                    created = true;
                    return methodInfo;
                }
            }

            public TDelegate GetOrCreateDelegate<TDelegate>(MethodInfo methodInfo, bool? expectStatic, out bool created)
                where TDelegate : Delegate
            {
                if (methodInfo == null)
                {
                    throw new ArgumentNullException(nameof(methodInfo));
                }

                created = false;
                EnsureMethodMatchesExpectation(methodInfo, expectStatic);

                if (_delegate is TDelegate typedDelegate)
                {
                    return typedDelegate;
                }

                if (_delegate != null && _delegate is not TDelegate)
                {
                    throw new AccessHelperException($"方法 {methodInfo.Name} 已缓存不同签名的委托，无法转换为 {typeof(TDelegate).FullName}。");
                }

                lock (_syncRoot)
                {
                    if (_delegate is TDelegate existing)
                    {
                        return existing;
                    }

                    var createdDelegate = AccessTools.MethodDelegate<TDelegate>(methodInfo);
                    _delegate = createdDelegate;
                    created = true;
                    return createdDelegate;
                }
            }

            private void EnsureMethodMatchesExpectation(MethodInfo methodInfo, bool? expectStatic)
            {
                if (!expectStatic.HasValue)
                {
                    return;
                }

                var actualStatic = _isStatic ?? methodInfo.IsStatic;
                if (actualStatic != expectStatic.Value)
                {
                    var message = expectStatic.Value
                        ? $"方法 {methodInfo.Name} 并非静态方法，无法通过静态成员访问。"
                        : $"方法 {methodInfo.Name} 是静态方法，无法通过实例成员访问。";
                    throw new AccessHelperException(message);
                }
            }
        }
    }
}

