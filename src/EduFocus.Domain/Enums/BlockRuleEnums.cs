namespace EduFocus.Domain.Enums;

public enum BlockRuleType
{
    Domain,
    App,
    Url
}

public enum BlockMatchType
{
    Exact,
    Suffix,
    Contains
}

public enum BlockRuleCategory
{
    Chatbot,
    Social,
    Game,
    Study,
    System,
    Custom
}

public enum BlockRuleEffect
{
    Block,
    Allow
}

public enum RuleEnforcementStatus
{
    Enforced,
    NotEnforced
}

public enum BlockDecision
{
    None,
    Allow,
    Block
}
