#define ACCESS_HELPER_ENABLE_CACHE_STATS

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
#if ACCESS_HELPER_ENABLE_CACHE_STATS
using System.Threading;
#endif
using HarmonyLib;
using EscapeFromDuckovCoopMod.Utils.Logger.Tools;

namespace EscapeFromDuckovCoopMod.Utils.AccessHelper
{
    /// <summary>
    /// 封装 Harmony <see cref="AccessTools"/> 的常用访问能力，提供统一的缓存、错误处理和链式调用接口。
    /// </summary>
    public static partial class AccessHelper
    {
        // 字段缓存条目，统一保存 FieldInfo 与相关委托。
        private static readonly ConcurrentDictionary<FieldCacheKey, FieldCacheEntry> FieldCache = new();
        // 方法缓存条目，统一保存 MethodInfo 与调用委托。
        private static readonly ConcurrentDictionary<MethodCacheKey, MethodCacheEntry> MethodCache = new();
        // 属性缓存条目，统一保存 PropertyInfo 与访问委托。
        private static readonly ConcurrentDictionary<PropertyCacheKey, PropertyCacheEntry> PropertyCache = new();

        /// <summary>
        /// 获取或设置 AccessHelper 的错误处理模式，控制异常抛出/吞噬。
        /// </summary>
        public static AccessHelperErrorHandlingMode ErrorMode
        {
            get { return ErrorHandler.Mode; }
            set { ErrorHandler.Mode = value; }
        }

        /// <summary>
        /// 获取或设置错误日志回调，当错误模式允许时由 AccessHelper 调用此回调记录异常信息。
        /// </summary>
        public static Action<AccessHelperException> ErrorLoggingCallback
        {
            get { return ErrorHandler.Logger; }
            set { ErrorHandler.Logger = value; }
        }

        /// <summary>
        /// 为指定的泛型声明类型构建链式访问入口。
        /// </summary>
        public static TypeChain<TDeclaring> For<TDeclaring>()
        {
            return new TypeChain<TDeclaring>(typeof(TDeclaring));
        }

        /// <summary>
        /// 为运行时提供的声明类型构建链式访问入口。
        /// </summary>
        public static TypeChain For(Type declaringType)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            return new TypeChain(declaringType);
        }

        public static class Instance
        {
            #region 字段
            
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

                var key = new FieldCacheKey(declaringType, fieldName);
                var cacheEntry = FieldCache.GetOrAdd(key, static _ => new FieldCacheEntry());

                try
                {
                    var fieldInfo = cacheEntry.GetOrCreateInfo(key, false, out var created);
                    RecordFieldCacheStatistics(created);
                    return fieldInfo;
                }
                catch (AccessHelperException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<FieldInfo>(declaringType, fieldName, exception);
                }
            }

            public static AccessTools.FieldRef<TDeclaring, TField> GetFieldRef<TDeclaring, TField>(string fieldName)
            {
                fieldName = NormalizeMemberName(fieldName);

                var key = new FieldCacheKey(typeof(TDeclaring), fieldName);
                var cacheEntry = FieldCache.GetOrAdd(key, static _ => new FieldCacheEntry());

                try
                {
                    var fieldRef = cacheEntry.GetOrCreateInstanceFieldRef<TDeclaring, TField>(key, out var created);
                    RecordFieldCacheStatistics(created);
                    return fieldRef;
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<AccessTools.FieldRef<TDeclaring, TField>>(typeof(TDeclaring), fieldName, exception);
                }
            }
            
            public static TField GetFieldValue<TDeclaring,TField>(TDeclaring instance, string fieldName)
            {
                if (instance == null)
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                var fieldRef = GetFieldRef<TDeclaring, TField>(fieldName);
                try
                {
                    return fieldRef(instance);
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<TField>(typeof(TDeclaring), fieldName, exception);
                }
            }

            public static void SetFieldValue<TDeclaring,TField>(TDeclaring instance, string fieldName, TField value)
            {
                if (instance == null)
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                var fieldRef = GetFieldRef<TDeclaring, TField>(fieldName);
                try
                {
                    fieldRef(instance) = value;
                }
                catch (Exception exception)
                {
                    ErrorHandler.Report(typeof(TDeclaring), fieldName, exception);
                }
            }

            #endregion

            #region 属性
            
            public static PropertyInfo GetPropertyInfo(Type declaringType, string propertyName)
            {
                if (declaringType == null)
                {
                    throw new ArgumentNullException(nameof(declaringType));
                }

                propertyName = NormalizeMemberName(propertyName);

                var key = new PropertyCacheKey(declaringType, propertyName);
                var cacheEntry = PropertyCache.GetOrAdd(key, static _ => new PropertyCacheEntry());

                try
                {
                    var propertyInfo = cacheEntry.GetOrCreateInfo(key, false, out var created);
                    RecordPropertyCacheStatistics(created);
                    return propertyInfo;
                }
                catch (AccessHelperException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<PropertyInfo>(declaringType, propertyName, exception);
                }
            }

            public static TProperty GetPropertyValue<TProperty>(object instance, string propertyName)
            {
                if (instance == null)
                {
                    throw new ArgumentNullException(nameof(instance));
                }

                var propertyInfo = GetPropertyInfo(instance.GetType(), propertyName);
                var getter = propertyInfo.GetGetMethod(true);
                if (getter == null)
                {
                    throw new MissingMethodException(propertyInfo.DeclaringType?.FullName, $"get_{propertyInfo.Name}");
                }

                try
                {
                    var rawValue = getter.Invoke(instance, Array.Empty<object>());
                    return CastPropertyValue<TProperty>(rawValue, propertyInfo);
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<TProperty>(propertyInfo.DeclaringType, propertyInfo.Name, exception);
                }
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

                try
                {
                    setter.Invoke(instance, new object[] { value });
                }
                catch (Exception exception)
                {
                    ErrorHandler.Report(propertyInfo.DeclaringType, propertyInfo.Name, exception);
                }
            }
            
            #endregion

            #region 方法
            
            public static MethodInfo GetMethodInfo(Type declaringType, string methodName, Type[] parameterTypes)
            {
                if (declaringType == null)
                {
                    throw new ArgumentNullException(nameof(declaringType));
                }

                methodName = NormalizeMemberName(methodName);
                var normalizedParameterTypes = NormalizeParameterTypes(parameterTypes);

                var key = new MethodCacheKey(declaringType, methodName, normalizedParameterTypes);
                var cacheEntry = MethodCache.GetOrAdd(key, static _ => new MethodCacheEntry());

                try
                {
                    var methodInfo = cacheEntry.GetOrCreateInfo(key, false, out var created);
                    RecordMethodCacheStatistics(created);
                    return methodInfo;
                }
                catch (AccessHelperException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<MethodInfo>(declaringType, methodName, exception);
                }
            }

            public static TDelegate GetMethodDelegate<TDelegate>(Type declaringType, string methodName, params Type[] parameterTypes)
                where TDelegate : Delegate
            {
                var normalizedParameterTypes = NormalizeParameterTypes(parameterTypes);
                var key = new MethodCacheKey(declaringType, methodName, normalizedParameterTypes);
                var cacheEntry = MethodCache.GetOrAdd(key, static _ => new MethodCacheEntry());

                try
                {
                    var methodInfo = cacheEntry.GetOrCreateInfo(key, false, out var infoCreated);
                    var methodDelegate = cacheEntry.GetOrCreateDelegate<TDelegate>(methodInfo, false, out var delegateCreated);
                    RecordMethodCacheStatistics(infoCreated || delegateCreated);
                    return methodDelegate;
                }
                catch (AccessHelperException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<TDelegate>(declaringType, methodName, exception);
                }
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

                var args = parameters ?? Array.Empty<object>();
                var methodInfo = ResolveMethod(instance.GetType(), methodName, args, false);
                var result = methodInfo.Invoke(instance, args);
                return CastReturnValue<TReturn>(result, methodInfo);
            }
            
            #endregion
        }
        
        public static class Static
        {
            #region 字段

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

                var key = new FieldCacheKey(declaringType, fieldName);
                var cacheEntry = FieldCache.GetOrAdd(key, static _ => new FieldCacheEntry());

                try
                {
                    var fieldInfo = cacheEntry.GetOrCreateInfo(key, true, out var created);
                    RecordFieldCacheStatistics(created);
                    return fieldInfo;
                }
                catch (AccessHelperException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<FieldInfo>(declaringType, fieldName, exception);
                }
            }
            
            public static AccessTools.FieldRef<TField> GetFieldRef<TDeclaring, TField>(string fieldName)
            {
                fieldName = NormalizeMemberName(fieldName);

                var key = new FieldCacheKey(typeof(TDeclaring), fieldName);
                var cacheEntry = FieldCache.GetOrAdd(key, static _ => new FieldCacheEntry());

                try
                {
                    var fieldRef = cacheEntry.GetOrCreateStaticFieldRef<TField>(key, out var created);
                    RecordFieldCacheStatistics(created);
                    return fieldRef;
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<AccessTools.FieldRef<TField>>(typeof(TDeclaring), fieldName, exception);
                }
            }

            public static TField GetFieldValue<TDeclaring,TField>(string fieldName)
            {
                var fieldRef = GetFieldRef<TDeclaring, TField>(fieldName);
                try
                {
                    return fieldRef();
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<TField>(typeof(TDeclaring), fieldName, exception);
                }
            }

            public static void SetFieldValue<TDeclaring,TField>(string fieldName, TField value)
            {
                var fieldRef = GetFieldRef<TDeclaring, TField>(fieldName);
                try
                {
                    fieldRef() = value;
                }
                catch (Exception exception)
                {
                    ErrorHandler.Report(typeof(TDeclaring), fieldName, exception);
                }
            }
            
            #endregion

            #region 属性
            
            public static PropertyInfo GetPropertyInfo(Type declaringType, string propertyName)
            {
                if (declaringType == null)
                {
                    throw new ArgumentNullException(nameof(declaringType));
                }

                propertyName = NormalizeMemberName(propertyName);

                var key = new PropertyCacheKey(declaringType, propertyName);
                var cacheEntry = PropertyCache.GetOrAdd(key, static _ => new PropertyCacheEntry());

                try
                {
                    var propertyInfo = cacheEntry.GetOrCreateInfo(key, true, out var created);
                    RecordPropertyCacheStatistics(created);
                    return propertyInfo;
                }
                catch (AccessHelperException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<PropertyInfo>(declaringType, propertyName, exception);
                }
            }

            public static TProperty GetPropertyValue<TProperty>(Type declaringType, string propertyName)
            {
                var propertyInfo = GetPropertyInfo(declaringType, propertyName);
                var getter = propertyInfo.GetGetMethod(true);
                if (getter == null)
                {
                    throw new MissingMethodException(propertyInfo.DeclaringType?.FullName, $"get_{propertyInfo.Name}");
                }

                try
                {
                    var rawValue = getter.Invoke(null, Array.Empty<object>());
                    return CastPropertyValue<TProperty>(rawValue, propertyInfo);
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<TProperty>(propertyInfo.DeclaringType, propertyInfo.Name, exception);
                }
            }

            public static void SetPropertyValue<TProperty>(Type declaringType, string propertyName, TProperty value)
            {
                var propertyInfo = GetPropertyInfo(declaringType, propertyName);
                var setter = propertyInfo.GetSetMethod(true);
                if (setter == null)
                {
                    throw new MissingMethodException(propertyInfo.DeclaringType?.FullName, $"set_{propertyInfo.Name}");
                }

                try
                {
                    setter.Invoke(null, new object[] { value });
                }
                catch (Exception exception)
                {
                    ErrorHandler.Report(propertyInfo.DeclaringType, propertyInfo.Name, exception);
                }
            }

            #endregion

            #region 方法
            
            public static MethodInfo GetMethodInfo(Type declaringType, string methodName, Type[] parameterTypes)
            {
                if (declaringType == null)
                {
                    throw new ArgumentNullException(nameof(declaringType));
                }

                methodName = NormalizeMemberName(methodName);
                var normalizedParameterTypes = NormalizeParameterTypes(parameterTypes);

                var key = new MethodCacheKey(declaringType, methodName, normalizedParameterTypes);
                var cacheEntry = MethodCache.GetOrAdd(key, static _ => new MethodCacheEntry());

                try
                {
                    var methodInfo = cacheEntry.GetOrCreateInfo(key, true, out var created);
                    RecordMethodCacheStatistics(created);
                    return methodInfo;
                }
                catch (AccessHelperException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<MethodInfo>(declaringType, methodName, exception);
                }
            }

            public static TDelegate GetMethodDelegate<TDelegate>(Type declaringType, string methodName, params Type[] parameterTypes)
                where TDelegate : Delegate
            {
                var normalizedParameterTypes = NormalizeParameterTypes(parameterTypes);
                var key = new MethodCacheKey(declaringType, methodName, normalizedParameterTypes);
                var cacheEntry = MethodCache.GetOrAdd(key, static _ => new MethodCacheEntry());

                try
                {
                    var methodInfo = cacheEntry.GetOrCreateInfo(key, true, out var infoCreated);
                    var methodDelegate = cacheEntry.GetOrCreateDelegate<TDelegate>(methodInfo, true, out var delegateCreated);
                    RecordMethodCacheStatistics(infoCreated || delegateCreated);
                    return methodDelegate;
                }
                catch (AccessHelperException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return ErrorHandler.Report<TDelegate>(declaringType, methodName, exception);
                }
            }

            public static object InvokeMethod(Type declaringType, string methodName, params object[] parameters)
            {
                return InvokeMethod<object>(declaringType, methodName, parameters);
            }

            public static TReturn InvokeMethod<TReturn>(Type declaringType, string methodName, params object[] parameters)
            {
                var args = parameters ?? Array.Empty<object>();
                var methodInfo = ResolveMethod(declaringType, methodName, args, true);
                var result = methodInfo.Invoke(null, args);
                return CastReturnValue<TReturn>(result, methodInfo);
            }
            #endregion
        }
        
        /// <summary>
        /// 通过预热字段信息的方式提前填充缓存，降低首轮访问的反射开销。
        /// </summary>
        public static void WarmupFields(params (Type DeclaringType, string FieldName)[] targets)
        {
            if (targets == null || targets.Length == 0)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (target.DeclaringType == null)
                {
                    continue;
                }

                try
                {
                    Instance.GetFieldInfo(target.DeclaringType, target.FieldName);
                    Static.GetFieldInfo(target.DeclaringType, target.FieldName);
                }
                catch
                {
                    // 预热阶段忽略异常，保持容错
                }
            }
        }

        /// <summary>
        /// 预热指定的属性缓存，适用于初始化阶段的批量加载。
        /// </summary>
        public static void WarmupProperties(params (Type DeclaringType, string PropertyName)[] targets)
        {
            if (targets == null || targets.Length == 0)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (target.DeclaringType == null)
                {
                    continue;
                }

                try
                {
                    Instance.GetPropertyInfo(target.DeclaringType, target.PropertyName);
                    Static.GetPropertyInfo(target.DeclaringType, target.PropertyName);
                }
                catch
                {
                    // 预热阶段忽略异常，保持容错
                }
            }
        }
        
        /// <summary>
        /// 预热指定的方法缓存，可包含参数类型以便准确定位重载。
        /// </summary>
        public static void WarmupMethods(params (Type DeclaringType, string MethodName, Type[] ParameterTypes)[] targets)
        {
            if (targets == null || targets.Length == 0)
            {
                return;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (target.DeclaringType == null)
                {
                    continue;
                }

                try
                {
                    var parameterTypes = target.ParameterTypes ?? Array.Empty<Type>();
                    Instance.GetMethodInfo(target.DeclaringType, target.MethodName, parameterTypes);
                    Static.GetMethodInfo(target.DeclaringType, target.MethodName, parameterTypes);
                }
                catch
                {
                    // 预热阶段忽略异常，保持容错
                }
            }
        }
        
        /// <summary>
        /// 清空所有缓存并重置统计信息，适用于热重载或资源清理场景。
        /// </summary>
        public static void ClearCaches()
        {
            FieldCache.Clear();
            MethodCache.Clear();
            PropertyCache.Clear();
            CacheStatistics.Reset();
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

        private static MethodInfo ResolveMethod(Type declaringType, string methodName, object[] parameters, bool expectStatic)
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
                return expectStatic
                    ? Static.GetMethodInfo(declaringType, methodName, parameterTypes)
                    : Instance.GetMethodInfo(declaringType, methodName, parameterTypes);
            }
            catch (MissingMethodException) when (parameterTypes.Length == 0)
            {
                // 当无法推断参数类型或未指定参数类型时，尝试无参方法
                return expectStatic
                    ? Static.GetMethodInfo(declaringType, methodName, Array.Empty<Type>())
                    : Instance.GetMethodInfo(declaringType, methodName, Array.Empty<Type>());
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

        /// <summary>
        /// 面向泛型声明类型的链式调用入口。
        /// </summary>
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

            /// <summary>
            /// 获取链式上下文中的声明类型。
            /// </summary>
            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            /// <summary>
            /// 进入字段访问链式接口。
            /// </summary>
            public FieldChain<TDeclaring> Field(string fieldName)
            {
                return new FieldChain<TDeclaring>(_declaringType, fieldName);
            }

            /// <summary>
            /// 进入属性访问链式接口。
            /// </summary>
            public PropertyChain<TDeclaring> Property(string propertyName)
            {
                return new PropertyChain<TDeclaring>(_declaringType, propertyName);
            }

            /// <summary>
            /// 进入方法访问链式接口。
            /// </summary>
            public MethodChain<TDeclaring> Method(string methodName)
            {
                return new MethodChain<TDeclaring>(_declaringType, methodName, null);
            }
        }

        /// <summary>
        /// 面向运行时类型对象的链式调用入口。
        /// </summary>
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

            /// <summary>
            /// 获取链式上下文中的声明类型。
            /// </summary>
            public Type DeclaringType
            {
                get { return _declaringType; }
            }

            /// <summary>
            /// 进入字段访问链式接口。
            /// </summary>
            public FieldChain<object> Field(string fieldName)
            {
                return new FieldChain<object>(_declaringType, fieldName);
            }

            /// <summary>
            /// 进入属性访问链式接口。
            /// </summary>
            public PropertyChain<object> Property(string propertyName)
            {
                return new PropertyChain<object>(_declaringType, propertyName);
            }

            /// <summary>
            /// 进入方法访问链式接口。
            /// </summary>
            public MethodChain<object> Method(string methodName)
            {
                return new MethodChain<object>(_declaringType, methodName, null);
            }
        }

        #region 缓存状态统计
        
        [Conditional("ACCESS_HELPER_ENABLE_CACHE_STATS")]
        private static void RecordFieldCacheStatistics(bool created)
        {
            if (created)
            {
                CacheStatistics.RecordFieldMiss();
            }
            else
            {
                CacheStatistics.RecordFieldHit();
            }
        }

        [Conditional("ACCESS_HELPER_ENABLE_CACHE_STATS")]
        private static void RecordPropertyCacheStatistics(bool created)
        {
            if (created)
            {
                CacheStatistics.RecordPropertyMiss();
            }
            else
            {
                CacheStatistics.RecordPropertyHit();
            }
        }

        [Conditional("ACCESS_HELPER_ENABLE_CACHE_STATS")]
        private static void RecordMethodCacheStatistics(bool created)
        {
            if (created)
            {
                CacheStatistics.RecordMethodMiss();
            }
            else
            {
                CacheStatistics.RecordMethodHit();
            }
        }
        
        /// <summary>
        /// 获取当前缓存的统计数据快照。
        /// </summary>
        public static AccessHelperCacheStatistics GetCacheStatistics()
        {
            return CacheStatistics.Snapshot();
        }

        /// <summary>
        /// 将缓存统计计数器重置为初始状态。
        /// </summary>
        public static void ResetCacheStatistics()
        {
            CacheStatistics.Reset();
        }
        
        public readonly struct AccessHelperCacheStatistics
        {
#if ACCESS_HELPER_ENABLE_CACHE_STATS
            public AccessHelperCacheStatistics(long fieldHits, long fieldMisses, long propertyHits, long propertyMisses, long methodHits, long methodMisses)
            {
                FieldHits = fieldHits;
                FieldMisses = fieldMisses;
                PropertyHits = propertyHits;
                PropertyMisses = propertyMisses;
                MethodHits = methodHits;
                MethodMisses = methodMisses;
            }

            public long FieldHits { get; }

            public long FieldMisses { get; }

            public long PropertyHits { get; }

            public long PropertyMisses { get; }

            public long MethodHits { get; }

            public long MethodMisses { get; }
#else
            public long FieldHits => 0;

            public long FieldMisses => 0;

            public long PropertyHits => 0;

            public long PropertyMisses => 0;

            public long MethodHits => 0;

            public long MethodMisses => 0;
#endif
        }

        private static class CacheStatistics
        {
#if ACCESS_HELPER_ENABLE_CACHE_STATS
            private static long _fieldHits;
            private static long _fieldMisses;
            private static long _propertyHits;
            private static long _propertyMisses;
            private static long _methodHits;
            private static long _methodMisses;
#endif
            [Conditional("ACCESS_HELPER_ENABLE_CACHE_STATS")]
            public static void RecordFieldHit()
            {
#if ACCESS_HELPER_ENABLE_CACHE_STATS
                Interlocked.Increment(ref _fieldHits);
#endif
            }

            [Conditional("ACCESS_HELPER_ENABLE_CACHE_STATS")]
            public static void RecordFieldMiss()
            {
#if ACCESS_HELPER_ENABLE_CACHE_STATS
                Interlocked.Increment(ref _fieldMisses);
#endif
            }

            [Conditional("ACCESS_HELPER_ENABLE_CACHE_STATS")]
            public static void RecordPropertyHit()
            {
#if ACCESS_HELPER_ENABLE_CACHE_STATS
                Interlocked.Increment(ref _propertyHits);
#endif
            }

            [Conditional("ACCESS_HELPER_ENABLE_CACHE_STATS")]
            public static void RecordPropertyMiss()
            {
#if ACCESS_HELPER_ENABLE_CACHE_STATS
                Interlocked.Increment(ref _propertyMisses);
#endif
            }

            [Conditional("ACCESS_HELPER_ENABLE_CACHE_STATS")]
            public static void RecordMethodHit()
            {
#if ACCESS_HELPER_ENABLE_CACHE_STATS
                Interlocked.Increment(ref _methodHits);
#endif
            }

            [Conditional("ACCESS_HELPER_ENABLE_CACHE_STATS")]
            public static void RecordMethodMiss()
            {
#if ACCESS_HELPER_ENABLE_CACHE_STATS
                Interlocked.Increment(ref _methodMisses);
#endif
            }

            public static AccessHelperCacheStatistics Snapshot()
            {
#if ACCESS_HELPER_ENABLE_CACHE_STATS
                return new AccessHelperCacheStatistics(
                    Interlocked.Read(ref _fieldHits),
                    Interlocked.Read(ref _fieldMisses),
                    Interlocked.Read(ref _propertyHits),
                    Interlocked.Read(ref _propertyMisses),
                    Interlocked.Read(ref _methodHits),
                    Interlocked.Read(ref _methodMisses));
#else
                return new AccessHelperCacheStatistics();
#endif
            }

            [Conditional("ACCESS_HELPER_ENABLE_CACHE_STATS")]
            public static void Reset()
            {
#if ACCESS_HELPER_ENABLE_CACHE_STATS
                Interlocked.Exchange(ref _fieldHits, 0);
                Interlocked.Exchange(ref _fieldMisses, 0);
                Interlocked.Exchange(ref _propertyHits, 0);
                Interlocked.Exchange(ref _propertyMisses, 0);
                Interlocked.Exchange(ref _methodHits, 0);
                Interlocked.Exchange(ref _methodMisses, 0);
#endif
            }
        }
        
        #endregion
    }
}
