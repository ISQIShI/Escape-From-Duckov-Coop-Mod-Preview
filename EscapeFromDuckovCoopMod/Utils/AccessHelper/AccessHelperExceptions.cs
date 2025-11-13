using System;

namespace EscapeFromDuckovCoopMod.Utils.AccessHelper
{
    public class AccessHelperException : Exception
    {
        public AccessHelperException(string message)
            : base(message)
        {
        }

        public AccessHelperException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

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
