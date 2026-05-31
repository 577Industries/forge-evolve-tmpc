// FORGE EVOLVE for TMPC — CLAR node-type enums.
//
// Each enum mirrors a "type" enum from clar-spec/CLAR.schema.json EXACTLY (UPPER_SNAKE_CASE
// member names). They serialize via JsonStringEnumConverter to the schema's strings with no
// naming policy, so the emitter cannot drift from the frozen vocabulary. If the schema's
// enums ever change, these will fail to compile against the lifter and the test suite will
// catch the mismatch.

namespace ForgeEvolve.Clar.Model;

/// <summary>controlFlowNode.type vocabulary (frozen schema enum).</summary>
public enum ControlFlowType
{
    SEQUENCE,
    BRANCH,
    FOR_LOOP,
    WHILE_LOOP,
    DO_UNTIL,
    SWITCH,
    TRY_CATCH,
    PARALLEL,
    PIPELINE,
    EVENT_HANDLER,
    STATE_MACHINE,
    COROUTINE
}

/// <summary>dataFlowNode.type vocabulary (frozen schema enum).</summary>
public enum DataFlowType
{
    VARIABLE,
    CONSTANT,
    PARAMETER,
    RETURN_VALUE,
    ASSIGNMENT,
    ARITHMETIC,
    COMPARISON,
    CAST,
    AGGREGATE,
    COLLECTION,
    RECORD,
    FIXED_DECIMAL,
    FLOATING_POINT,
    STRING_OP,
    DATE_TIME
}

/// <summary>businessLogicNode.type vocabulary (frozen schema enum).</summary>
public enum BusinessLogicType
{
    RULE,
    CONSTRAINT,
    INVARIANT,
    VALIDATION,
    CALCULATION,
    CLASSIFICATION,
    ROUTING,
    AUTHORIZATION,
    AUDIT,
    NOTIFICATION
}

/// <summary>infrastructureNode.type vocabulary (frozen schema enum).</summary>
public enum InfrastructureType
{
    FILE_IO,
    DB_QUERY,
    API_CALL,
    MESSAGE_SEND,
    MESSAGE_RECEIVE,
    STREAM_PROCESS,
    BATCH_JOB,
    TIMER,
    LOGGING,
    CONFIGURATION
}
