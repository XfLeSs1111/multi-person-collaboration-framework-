using UnityEngine;

namespace Socket.Multiplayer
{
    /// <summary>
    /// 玩法规则资产基类。框架只认识"房间绑定了一个规则资产"，
    /// 并从中读取摘要、观战开关与协议签名指纹；
    /// 具体规则字段、规则集构造由各玩法子类实现（如 GomokuRuleConfig）。
    /// 新增玩法 = 派生一个规则资产子类，框架层零改动。
    /// </summary>
    public abstract class MatchRulesConfig : ScriptableObject
    {
        /// <summary>玩法名（用于配置中心标题与日志）。</summary>
        public abstract string RulesetName { get; }

        /// <summary>一行摘要，供房间模板 / 配置中心显示。</summary>
        public abstract string RulesSummary { get; }

        /// <summary>是否允许观战；默认允许，玩法可按自己的规则覆盖。</summary>
        public virtual bool AllowsSpectators => true;

        /// <summary>参与联机协议签名的规则指纹（参数一变签名即变，拦截版本不一致的客户端）。</summary>
        public virtual string SignatureFingerprint() => RulesetName;
    }
}
