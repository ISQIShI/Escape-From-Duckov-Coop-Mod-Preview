namespace EscapeFromDuckovCoopMod.Utils.NetHelper
{
    /// <summary>
    /// 消息优先级枚举
    /// </summary>
    public enum MessagePriority : byte
    {
        /// <summary>
        /// 关键功能：投票、伤害、拾取、交互
        /// - 必须送达
        /// - 严格有序
        /// - 使用 ReliableOrdered
        /// </summary>
        Critical = 0,

        /// <summary>
        /// 重要状态：血量、装备、弹药
        /// - 必须送达
        /// - 只保留最新
        /// - 使用 ReliableSequenced
        /// </summary>
        Important = 1,

        /// <summary>
        /// 普通事件：NPC 状态、物品生成、环境交互
        /// - 必须送达
        /// - 顺序无关
        /// - 使用 ReliableUnordered
        /// </summary>
        Normal = 2,

        /// <summary>
        /// 高频更新：位置、旋转、姿态、动画
        /// - 可以丢弃
        /// - 下一帧覆盖
        /// - 使用 Unreliable
        /// </summary>
        Frequent = 3,

        /// <summary>
        /// 语音数据：VOIP 语音流
        /// - 可以丢弃
        /// - 只保留最新
        /// - 使用 Sequenced
        /// </summary>
        Voice = 4
    }
}
