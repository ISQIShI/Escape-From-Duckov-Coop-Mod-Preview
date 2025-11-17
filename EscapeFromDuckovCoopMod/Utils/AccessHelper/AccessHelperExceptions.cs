using System;

namespace EscapeFromDuckovCoopMod.Utils.AccessHelper
{
    /// <summary>
    /// AccessHelper 统一的异常基类，封装反射访问过程中产生的错误信息。
    /// </summary>
    public class AccessHelperException : Exception
    {
        /// <summary>
        /// 使用自定义错误消息初始化异常。
        /// </summary>
        public AccessHelperException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 使用错误消息与内部异常初始化异常。
        /// </summary>
        public AccessHelperException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// 在目标类型中找不到指定字段时抛出的异常。
    /// </summary>
    public class FieldNotFoundException : AccessHelperException
    {
        public FieldNotFoundException(Type declaringType, string fieldName)
            : base($"未能在类型 {declaringType?.FullName ?? "<null>"} 中找到字段 {fieldName ?? "<null>"}。")
        {
            DeclaringType = declaringType;
            FieldName = fieldName;
        }

        public Type DeclaringType { get; }

        public string FieldName { get; }
    }

    /// <summary>
    /// 在目标类型中找不到指定方法时抛出的异常。
    /// </summary>
    public class MethodNotFoundException : AccessHelperException
    {
        public MethodNotFoundException(Type declaringType, string methodName)
            : base($"未能在类型 {declaringType?.FullName ?? "<null>"} 中找到方法 {methodName ?? "<null>"}。")
        {
            DeclaringType = declaringType;
            MethodName = methodName;
        }

        public MethodNotFoundException(Type declaringType, string methodName, Type[] parameterTypes)
            : base($"未能在类型 {declaringType?.FullName ?? "<null>"} 中找到方法 {methodName ?? "<null>"}，参数类型: {BuildParameterList(parameterTypes)}。")
        {
            DeclaringType = declaringType;
            MethodName = methodName;
            ParameterTypes = parameterTypes ?? Array.Empty<Type>();
        }

        public Type DeclaringType { get; }

        public string MethodName { get; }

        public Type[] ParameterTypes { get; }

        private static string BuildParameterList(Type[] parameterTypes)
        {
            if (parameterTypes == null || parameterTypes.Length == 0)
            {
                return "<empty>";
            }

            var names = new string[parameterTypes.Length];
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                names[i] = parameterTypes[i]?.FullName ?? "<null>";
            }

            return string.Join(", ", names);
        }
    }

    /// <summary>
    /// 在目标类型中找不到指定属性时抛出的异常。
    /// </summary>
    public class PropertyNotFoundException : AccessHelperException
    {
        public PropertyNotFoundException(Type declaringType, string propertyName)
            : base($"未能在类型 {declaringType?.FullName ?? "<null>"} 中找到属性 {propertyName ?? "<null>"}。")
        {
            DeclaringType = declaringType;
            PropertyName = propertyName;
        }

        public Type DeclaringType { get; }

        public string PropertyName { get; }
    }

    /// <summary>
    /// 当成员的实际类型与调用方期望不一致时抛出的异常。
    /// </summary>
    public class TypeMismatchException : AccessHelperException
    {
        public TypeMismatchException(string memberName, Type expectedType, Type actualType)
            : base($"成员 {memberName ?? "<null>"} 的实际类型 {actualType?.FullName ?? "<null>"} 与期望类型 {expectedType?.FullName ?? "<null>"} 不匹配。")
        {
            MemberName = memberName;
            ExpectedType = expectedType;
            ActualType = actualType;
        }

        public string MemberName { get; }

        public Type ExpectedType { get; }

        public Type ActualType { get; }
    }
}
