using System;
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using EscapeFromDuckovCoopMod.Utils.Logger.Tools;

namespace EscapeFromDuckovCoopMod.Utils.AccessHelper
{
    public static class AccessHelper
    {
        private static readonly ConcurrentDictionary<(Type DeclaringType, string MemberName), FieldInfo> FieldCache = new();
        private static readonly ConcurrentDictionary<(Type DeclaringType, string MemberName), Delegate> FieldRefCache = new();
        private static readonly ConcurrentDictionary<MethodCacheKey, MethodInfo> MethodCache = new();
        private static readonly ConcurrentDictionary<(Type DeclaringType, string MemberName), PropertyInfo> PropertyCache = new();

        public static AccessHelperErrorHandlingMode ErrorMode
        {
            get { return ErrorHandler.Mode; }
            set { ErrorHandler.Mode = value; }
        }

        public static Action<AccessHelperException> ErrorLoggingCallback
        {
            get { return ErrorHandler.Logger; }
            set { ErrorHandler.Logger = value; }
        }

        public static TypeChain<TDeclaring> For<TDeclaring>()
        {
            return new TypeChain<TDeclaring>(typeof(TDeclaring));
        }

        public static TypeChain For(Type declaringType)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            return new TypeChain(declaringType);
        }

        public static MethodInfo GetMethodInfo<TDeclaring>(string methodName, params Type[] parameterTypes)
        {
            return GetMethodInfo(typeof(TDeclaring), methodName, parameterTypes);
        }

        public static MethodInfo GetMethodInfo(Type declaringType, string methodName, params Type[] parameterTypes)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            methodName = NormalizeMemberName(methodName);
            parameterTypes = NormalizeParameterTypes(parameterTypes);

            var key = new MethodCacheKey(declaringType, methodName, parameterTypes);

            if (MethodCache.TryGetValue(key, out var cachedMethod))
            {
                return cachedMethod;
            }

            MethodInfo methodInfo = null;
            var parameters = parameterTypes.Length == 0 ? null : parameterTypes;

            try
            {
                methodInfo = AccessTools.Method(declaringType, methodName, parameters);
                if (methodInfo == null)
                {
                    throw new MethodNotFoundException(declaringType, methodName, parameters ?? Array.Empty<Type>());
                }
            }
            catch (Exception exception)
            {
                return ErrorHandler.Report<MethodInfo>(declaringType, methodName, exception);
            }

            if (methodInfo != null)
            {
                MethodCache.TryAdd(key, methodInfo);
            }

            return methodInfo;
        }

        public static PropertyInfo GetPropertyInfo<TDeclaring>(string propertyName)
        {
            return GetPropertyInfo(typeof(TDeclaring), propertyName);
        }

        public static PropertyInfo GetPropertyInfo(Type declaringType, string propertyName)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            propertyName = NormalizeMemberName(propertyName);

            var key = (declaringType, propertyName);

            if (PropertyCache.TryGetValue(key, out var cachedProperty))
            {
                return cachedProperty;
            }

            PropertyInfo propertyInfo = null;

            try
            {
                propertyInfo = AccessTools.Property(declaringType, propertyName);
                if (propertyInfo == null)
                {
                    throw new PropertyNotFoundException(declaringType, propertyName);
                }
            }
            catch (Exception exception)
            {
                return ErrorHandler.Report<PropertyInfo>(declaringType, propertyName, exception);
            }

            if (propertyInfo != null)
            {
                PropertyCache.TryAdd(key, propertyInfo);
            }

            return propertyInfo;
        }

        public static FieldInfo GetFieldInfo<TDeclaring>(string fieldName)
        {
            return GetFieldInfo(typeof(TDeclaring), fieldName);
        }

        public static FieldInfo GetFieldInfo(Type declaringType, string fieldName)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            fieldName = NormalizeMemberName(fieldName);

            var key = (declaringType, fieldName);

            if (FieldCache.TryGetValue(key, out var cachedField))
            {
                return cachedField;
            }

            FieldInfo fieldInfo = null;

            try
            {
                fieldInfo = AccessTools.Field(declaringType, fieldName);
                if (fieldInfo == null)
                {
                    throw new FieldNotFoundException(declaringType, fieldName);
                }
            }
            catch (Exception exception)
            {
                return ErrorHandler.Report<FieldInfo>(declaringType, fieldName, exception);
            }

            if (fieldInfo != null)
            {
                FieldCache.TryAdd(key, fieldInfo);
            }

            return fieldInfo;
        }

        public static TField GetFieldValue<TField>(object instance, string fieldName)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            var fieldInfo = GetFieldInfo(instance.GetType(), fieldName);
            if (fieldInfo == null)
            {
                return default(TField);
            }

            if (fieldInfo.IsStatic)
            {
                return ErrorHandler.Report<TField>(fieldInfo.DeclaringType, fieldInfo.Name, new AccessHelperException($"字段 {fieldInfo.Name} 是静态字段，请使用 GetStaticFieldValue。"));
            }

            try
            {
                var rawValue = fieldInfo.GetValue(instance);
                return CastValue<TField>(rawValue, fieldInfo);
            }
            catch (Exception exception)
            {
                return ErrorHandler.Report<TField>(fieldInfo.DeclaringType, fieldInfo.Name, exception);
            }
        }

        public static TField GetStaticFieldValue<TField>(Type declaringType, string fieldName)
        {
            var fieldInfo = GetFieldInfo(declaringType, fieldName);
            if (fieldInfo == null)
            {
                return default(TField);
            }

            if (!fieldInfo.IsStatic)
            {
                return ErrorHandler.Report<TField>(fieldInfo.DeclaringType, fieldInfo.Name, new AccessHelperException($"字段 {fieldInfo.Name} 不是静态字段，请使用 GetFieldValue。"));
            }

            try
            {
                var rawValue = fieldInfo.GetValue(null);
                return CastValue<TField>(rawValue, fieldInfo);
            }
            catch (Exception exception)
            {
                return ErrorHandler.Report<TField>(fieldInfo.DeclaringType, fieldInfo.Name, exception);
            }
        }

        public static void SetFieldValue<TField>(object instance, string fieldName, TField value)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            var fieldInfo = GetFieldInfo(instance.GetType(), fieldName);
            if (fieldInfo == null)
            {
                return;
            }

            if (fieldInfo.IsStatic)
            {
                ErrorHandler.Report(fieldInfo.DeclaringType, fieldInfo.Name, new AccessHelperException($"字段 {fieldInfo.Name} 是静态字段，请使用 SetStaticFieldValue。"));
                return;
            }

            try
            {
                fieldInfo.SetValue(instance, value);
            }
            catch (Exception exception)
            {
                ErrorHandler.Report(fieldInfo.DeclaringType, fieldInfo.Name, exception);
            }
        }

        public static void SetStaticFieldValue<TField>(Type declaringType, string fieldName, TField value)
        {
            var fieldInfo = GetFieldInfo(declaringType, fieldName);
            if (fieldInfo == null)
            {
                return;
            }

            if (!fieldInfo.IsStatic)
            {
                ErrorHandler.Report(fieldInfo.DeclaringType, fieldInfo.Name, new AccessHelperException($"字段 {fieldInfo.Name} 不是静态字段，请使用 SetFieldValue。"));
                return;
            }

            try
            {
                fieldInfo.SetValue(null, value);
            }
            catch (Exception exception)
            {
                ErrorHandler.Report(fieldInfo.DeclaringType, fieldInfo.Name, exception);
            }
        }

        public static AccessTools.FieldRef<TInstance, TField> GetFieldRef<TInstance, TField>(string fieldName)
        {
            fieldName = NormalizeMemberName(fieldName);
            var key = (typeof(TInstance), fieldName);

            var fieldRef = FieldRefCache.GetOrAdd(key, static k =>
            {
                return AccessTools.FieldRefAccess<TInstance, TField>(k.MemberName);
            });

            return (AccessTools.FieldRef<TInstance, TField>)fieldRef;
        }

        public static object InvokeMethod(object instance, string methodName, params object[] parameters)
        {
            return InvokeMethod<object>(instance, methodName, parameters);
        }

        public static TReturn InvokeMethod<TReturn>(object instance, string methodName, params object[] parameters)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            if (parameters == null)
            {
                parameters = Array.Empty<object>();
            }

            var methodInfo = ResolveMethod(instance.GetType(), methodName, parameters);
            var result = methodInfo.Invoke(instance, parameters);
            return CastReturnValue<TReturn>(result, methodInfo);
        }

        public static object InvokeStaticMethod(Type declaringType, string methodName, params object[] parameters)
        {
            return InvokeStaticMethod<object>(declaringType, methodName, parameters);
        }

        public static TReturn InvokeStaticMethod<TReturn>(Type declaringType, string methodName, params object[] parameters)
        {
            if (parameters == null)
            {
                parameters = Array.Empty<object>();
            }

            var methodInfo = ResolveMethod(declaringType, methodName, parameters);
            if (!methodInfo.IsStatic)
            {
                throw new InvalidOperationException($"方法 {methodInfo.Name} 不是静态方法，请调用 InvokeMethod。 声明类型: {methodInfo.DeclaringType?.FullName}");
            }

            var result = methodInfo.Invoke(null, parameters);
            return CastReturnValue<TReturn>(result, methodInfo);
        }

        public static TProperty GetPropertyValue<TProperty>(object instance, string propertyName)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            var propertyInfo = GetPropertyInfo(instance.GetType(), propertyName);
            if (propertyInfo.GetGetMethod(true) == null)
            {
                throw new MissingMethodException(propertyInfo.DeclaringType?.FullName, $"get_{propertyInfo.Name}");
            }

            var rawValue = propertyInfo.GetValue(instance);
            return CastPropertyValue<TProperty>(rawValue, propertyInfo);
        }

        public static TProperty GetStaticPropertyValue<TProperty>(Type declaringType, string propertyName)
        {
            var propertyInfo = GetPropertyInfo(declaringType, propertyName);
            if (!IsStaticProperty(propertyInfo))
            {
                throw new InvalidOperationException($"属性 {propertyInfo.Name} 不是静态属性，请调用 GetPropertyValue。 声明类型: {propertyInfo.DeclaringType?.FullName}");
            }

            if (propertyInfo.GetGetMethod(true) == null)
            {
                throw new MissingMethodException(propertyInfo.DeclaringType?.FullName, $"get_{propertyInfo.Name}");
            }

            var rawValue = propertyInfo.GetValue(null);
            return CastPropertyValue<TProperty>(rawValue, propertyInfo);
        }

        public static void SetPropertyValue<TProperty>(object instance, string propertyName, TProperty value)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            var propertyInfo = GetPropertyInfo(instance.GetType(), propertyName);
            var setter = propertyInfo.GetSetMethod(true);
            if (setter == null)
            {
                throw new MissingMethodException(propertyInfo.DeclaringType?.FullName, $"set_{propertyInfo.Name}");
            }

            setter.Invoke(instance, new object[] { value });
        }

        public static void SetStaticPropertyValue<TProperty>(Type declaringType, string propertyName, TProperty value)
        {
            var propertyInfo = GetPropertyInfo(declaringType, propertyName);
            if (!IsStaticProperty(propertyInfo))
            {
                throw new InvalidOperationException($"属性 {propertyInfo.Name} 不是静态属性，请调用 SetPropertyValue。 声明类型: {propertyInfo.DeclaringType?.FullName}");
            }

            var setter = propertyInfo.GetSetMethod(true);
            if (setter == null)
            {
                throw new MissingMethodException(propertyInfo.DeclaringType?.FullName, $"set_{propertyInfo.Name}");
            }

            setter.Invoke(null, new object[] { value });
        }

        private static string NormalizeMemberName(string memberName)
        {
            if (string.IsNullOrWhiteSpace(memberName))
            {
                throw new ArgumentException("成员名称不能为空", nameof(memberName));
            }

            return memberName.Trim();
        }

        private static TField CastValue<TField>(object rawValue, FieldInfo fieldInfo)
        {
            if (rawValue is null)
            {
                if (default(TField) is null)
                {
                    return default(TField);
                }

                throw new InvalidCastException($"无法将字段 {fieldInfo.Name} 的值 null 转换为类型 {typeof(TField).FullName}");
            }

            if (rawValue is TField converted)
            {
                return converted;
            }

            throw new InvalidCastException($"无法将字段 {fieldInfo.Name} 的值从 {rawValue.GetType().FullName} 转换为 {typeof(TField).FullName}");
        }

        private static bool IsStaticProperty(PropertyInfo propertyInfo)
        {
            var getter = propertyInfo.GetGetMethod(true);
            var setter = propertyInfo.GetSetMethod(true);
            return (getter != null && getter.IsStatic) || (setter != null && setter.IsStatic);
        }

        private static MethodInfo ResolveMethod(Type declaringType, string methodName, object[] parameters)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            methodName = NormalizeMemberName(methodName);

            var actualParameters = parameters ?? Array.Empty<object>();
            var parameterTypes = InferParameterTypes(actualParameters);
            try
            {
                return GetMethodInfo(declaringType, methodName, parameterTypes);
            }
            catch (MissingMethodException) when (parameterTypes.Length == 0)
            {
                // 当无法推断参数类型或未指定参数类型时，尝试无参方法
                return GetMethodInfo(declaringType, methodName);
            }
        }

        private static Type[] NormalizeParameterTypes(Type[] parameterTypes)
        {
            if (parameterTypes == null || parameterTypes.Length == 0)
            {
                return Array.Empty<Type>();
            }

            var normalized = new Type[parameterTypes.Length];
            for (var i = 0; i < parameterTypes.Length; i++)
            {
                normalized[i] = parameterTypes[i] ?? throw new ArgumentNullException(nameof(parameterTypes), "参数类型数组中存在 null 值");
            }

            return normalized;
        }

        private static Type[] InferParameterTypes(object[] parameters)
        {
            if (parameters == null || parameters.Length == 0)
            {
                return Array.Empty<Type>();
            }

            var parameterTypes = new Type[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter == null)
                {
                    throw new ArgumentNullException(nameof(parameters), "无法从 null 推断参数类型，请显式提供参数类型");
                }

                parameterTypes[i] = parameter.GetType();
            }

            return parameterTypes;
        }

        private static TReturn CastReturnValue<TReturn>(object rawValue, MethodInfo methodInfo)
        {
            if (rawValue is null)
            {
                if (default(TReturn) is null)
                {
                    return default(TReturn);
                }

                throw new InvalidCastException($"无法将方法 {methodInfo.Name} 的返回值 null 转换为类型 {typeof(TReturn).FullName}");
            }

            if (rawValue is TReturn converted)
            {
                return converted;
            }

            throw new InvalidCastException($"无法将方法 {methodInfo.Name} 的返回值从 {rawValue.GetType().FullName} 转换为 {typeof(TReturn).FullName}");
        }

        private static TProperty CastPropertyValue<TProperty>(object rawValue, PropertyInfo propertyInfo)
        {
            if (rawValue is null)
            {
                if (default(TProperty) is null)
                {
                    return default(TProperty);
                }

                throw new InvalidCastException($"无法将属性 {propertyInfo.Name} 的值 null 转换为类型 {typeof(TProperty).FullName}");
            }

            if (rawValue is TProperty converted)
            {
                return converted;
            }

            throw new InvalidCastException($"无法将属性 {propertyInfo.Name} 的值从 {rawValue.GetType().FullName} 转换为 {typeof(TProperty).FullName}");
        }

        public readonly struct TypeChain<TDeclaring>
        {
            private readonly Type _declaringType;

            internal TypeChain(Type declaringType)
            {
                if (declaringType == null)
                {
                    throw new ArgumentNullException(nameof(declaringType));
                }

                _declaringType = declaringType;
            }

            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            public FieldChain<TDeclaring> Field(string fieldName)
            {
                return new FieldChain<TDeclaring>(_declaringType, fieldName);
            }

            public PropertyChain<TDeclaring> Property(string propertyName)
            {
                return new PropertyChain<TDeclaring>(_declaringType, propertyName);
            }

            public MethodChain<TDeclaring> Method(string methodName)
            {
                return new MethodChain<TDeclaring>(_declaringType, methodName, null);
            }
        }

        public readonly struct TypeChain
        {
            private readonly Type _declaringType;

            internal TypeChain(Type declaringType)
            {
                if (declaringType == null)
                {
                    throw new ArgumentNullException(nameof(declaringType));
                }

                _declaringType = declaringType;
            }

            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            public FieldChain<object> Field(string fieldName)
            {
                return new FieldChain<object>(_declaringType, fieldName);
            }

            public PropertyChain<object> Property(string propertyName)
            {
                return new PropertyChain<object>(_declaringType, propertyName);
            }

            public MethodChain<object> Method(string methodName)
            {
                return new MethodChain<object>(_declaringType, methodName, null);
            }
        }

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

            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            public string FieldName
            {
                get { return _fieldName; }
            }

            public FieldInfo Info
            {
                get { return GetFieldInfo(_declaringType, _fieldName); }
            }

            public TField GetValue<TField>(TDeclaring instance)
            {
                if (ReferenceEquals(instance, null))
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                return GetFieldValue<TField>(instance, _fieldName);
            }

            public void SetValue<TField>(TDeclaring instance, TField value)
            {
                if (ReferenceEquals(instance, null))
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                SetFieldValue(instance, _fieldName, value);
            }

            public TField GetStaticValue<TField>()
            {
                return GetStaticFieldValue<TField>(_declaringType, _fieldName);
            }

            public void SetStaticValue<TField>(TField value)
            {
                SetStaticFieldValue(_declaringType, _fieldName, value);
            }

            public AccessTools.FieldRef<TDeclaring, TField> GetFieldRef<TField>()
            {
                return GetFieldRef<TDeclaring, TField>(_fieldName);
            }
        }

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

            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            public string PropertyName
            {
                get { return _propertyName; }
            }

            public PropertyInfo Info
            {
                get { return GetPropertyInfo(_declaringType, _propertyName); }
            }

            public TProperty GetValue<TProperty>(TDeclaring instance)
            {
                if (ReferenceEquals(instance, null))
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                return GetPropertyValue<TProperty>(instance, _propertyName);
            }

            public void SetValue<TProperty>(TDeclaring instance, TProperty value)
            {
                if (ReferenceEquals(instance, null))
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                SetPropertyValue(instance, _propertyName, value);
            }

            public TProperty GetStaticValue<TProperty>()
            {
                return GetStaticPropertyValue<TProperty>(_declaringType, _propertyName);
            }

            public void SetStaticValue<TProperty>(TProperty value)
            {
                SetStaticPropertyValue(_declaringType, _propertyName, value);
            }
        }

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

            public MethodChain<TDeclaring> WithParameters(params Type[] parameterTypes)
            {
                return new MethodChain<TDeclaring>(_declaringType, _methodName, parameterTypes);
            }

            public MethodInfo Info
            {
                get
                {
                    if (_parameterTypes != null)
                    {
                        return GetMethodInfo(_declaringType, _methodName, _parameterTypes);
                    }

                    return GetMethodInfo(_declaringType, _methodName);
                }
            }

            public void Invoke(TDeclaring instance, params object[] parameters)
            {
                Invoke<object>(instance, parameters);
            }

            public TReturn Invoke<TReturn>(TDeclaring instance, params object[] parameters)
            {
                if (ReferenceEquals(instance, null))
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                var args = parameters ?? Array.Empty<object>();

                if (_parameterTypes != null)
                {
                    var methodInfo = GetMethodInfo(_declaringType, _methodName, _parameterTypes);
                    var result = methodInfo.Invoke(instance, args);
                    return CastReturnValue<TReturn>(result, methodInfo);
                }

                return InvokeMethod<TReturn>(instance, _methodName, args);
            }

            public void InvokeStatic(params object[] parameters)
            {
                InvokeStatic<object>(parameters);
            }

            public TReturn InvokeStatic<TReturn>(params object[] parameters)
            {
                var args = parameters ?? Array.Empty<object>();

                if (_parameterTypes != null)
                {
                    var methodInfo = GetMethodInfo(_declaringType, _methodName, _parameterTypes);
                    if (!methodInfo.IsStatic)
                    {
                        throw new InvalidOperationException($"方法 {_methodName} 不是静态方法，请调用 Invoke。");
                    }

                    var result = methodInfo.Invoke(null, args);
                    return CastReturnValue<TReturn>(result, methodInfo);
                }

                return InvokeStaticMethod<TReturn>(_declaringType, _methodName, args);
            }
        }

        private readonly struct MethodCacheKey : IEquatable<MethodCacheKey>
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
    }
}
