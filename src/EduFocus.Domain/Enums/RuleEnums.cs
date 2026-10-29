namespace EduFocus.Domain.Enums;

public enum RuleType
{
    Domain,
    App,
    Url
}

public enum RuleMatchType
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

public enum EnforcementStatus
{
    Enforced,
    NotEnforced
}
