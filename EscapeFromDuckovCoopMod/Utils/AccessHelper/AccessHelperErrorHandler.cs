using System;
using EscapeFromDuckovCoopMod.Utils.Logger.Tools;

namespace EscapeFromDuckovCoopMod.Utils.AccessHelper
{
    public enum AccessHelperErrorHandlingMode
    {
        Strict = 0,
        Lenient = 1,
        LogOnly = 2
    }

    internal static class ErrorHandler
    {
        private static readonly object SyncRoot = new object();
        private static AccessHelperErrorHandlingMode _mode = AccessHelperErrorHandlingMode.Strict;
        private static Action<AccessHelperException> _logger = DefaultLogger;

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
