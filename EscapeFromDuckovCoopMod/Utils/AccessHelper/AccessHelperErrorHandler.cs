using System;
using EscapeFromDuckovCoopMod.Utils.Logger.Tools;

namespace EscapeFromDuckovCoopMod.Utils.AccessHelper
{
    /// <summary>
    /// AccessHelper 错误处理模式枚举，控制异常的处理策略。
    /// </summary>
    public enum AccessHelperErrorHandlingMode
    {
        /// <summary>
        /// 严格模式：直接抛出异常。
        /// </summary>
        Strict = 0,
        /// <summary>
        /// 宽松模式：吞掉异常返回默认值。
        /// </summary>
        Lenient = 1,
    }

    /// <summary>
    /// AccessHelper 内部错误处理器，依据配置的模式统一处理反射异常。
    /// </summary>
    internal static class ErrorHandler
    {
        private static readonly object SyncRoot = new object();
        private static AccessHelperErrorHandlingMode _mode = AccessHelperErrorHandlingMode.Strict;
        private static Action<AccessHelperException> _logger = DefaultLogger;

        /// <summary>
        /// 当前错误处理模式，线程安全读写。
        /// </summary>
        public static AccessHelperErrorHandlingMode Mode
        {
            get
            {
                lock (SyncRoot)
                {
                    return _mode;
                }
            }
            set
            {
                lock (SyncRoot)
                {
                    _mode = value;
                }
            }
        }

        /// <summary>
        /// 错误日志回调委托，线程安全读写。
        /// </summary>
        public static Action<AccessHelperException> Logger
        {
            get
            {
                lock (SyncRoot)
                {
                    return _logger;
                }
            }
            set
            {
                lock (SyncRoot)
                {
                    _logger = value;
                }
            }
        }

        /// <summary>
        /// 根据当前模式处理异常，并返回泛型默认值。
        /// </summary>
        public static T Report<T>(Type declaringType, string memberName, Exception exception)
        {
            var mode = Mode;
            var helperException = NormalizeException(declaringType, memberName, exception);

            if (mode == AccessHelperErrorHandlingMode.Strict)
            {
                throw helperException;
            }

            Log(helperException);
            return default(T);
        }

        /// <summary>
        /// 根据当前模式处理异常（无返回值版本）。
        /// </summary>
        public static void Report(Type declaringType, string memberName, Exception exception)
        {
            var mode = Mode;
            var helperException = NormalizeException(declaringType, memberName, exception);

            if (mode == AccessHelperErrorHandlingMode.Strict)
            {
                throw helperException;
            }

            Log(helperException);
        }

        /// <summary>
        /// 将任意异常转换为 AccessHelperException，便于统一处理。
        /// </summary>
        private static AccessHelperException NormalizeException(Type declaringType, string memberName, Exception exception)
        {
            if (exception == null)
            {
                return new AccessHelperException("未知的 AccessHelper 错误。");
            }

            if (exception is AccessHelperException accessHelperException)
            {
                return accessHelperException;
            }

            string declaringTypeName = declaringType != null ? declaringType.FullName : "<null>";
            string targetMember = memberName ?? "<null>";
            string message = string.Format("访问 {0}.{1} 时发生错误: {2}", declaringTypeName, targetMember, exception.Message);
            return new AccessHelperException(message, exception);
        }

        /// <summary>
        /// 调用日志回调记录异常，若回调失败则写入默认日志。
        /// </summary>
        private static void Log(AccessHelperException exception)
        {
            var logger = Logger;
            if (logger == null)
            {
                return;
            }

            try
            {
                logger(exception);
            }
            catch
            {
                try
                {
                    LoggerHelper.LogError("[AccessHelper] 错误日志回调执行失败: " + exception);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// 默认的错误日志记录实现。
        /// </summary>
        private static void DefaultLogger(AccessHelperException exception)
        {
            if (exception == null)
            {
                return;
            }

            LoggerHelper.LogError("[AccessHelper] " + exception);
        }
    }
}
