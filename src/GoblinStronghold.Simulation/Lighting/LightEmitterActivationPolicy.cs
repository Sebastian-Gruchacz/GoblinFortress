namespace GoblinStronghold.Simulation.Lighting;

public readonly record struct LightEmitterActivationContext(
    bool IsWorking = false,
    bool HasWorkOrderFuel = false,
    bool HasStoredFuel = false,
    bool IsCarried = false,
    bool HasPortableCharge = false,
    bool IsActorTraitActive = false);

public static class LightEmitterActivationPolicy
{
    public static bool IsActive(
        LightEmitterDefinition definition,
        LightEmitterActivationContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var activitySatisfied = definition.Activation.Activity switch
        {
            LightEmitterActivityRequirement.Always => true,
            LightEmitterActivityRequirement.WhileWorking => context.IsWorking,
            LightEmitterActivityRequirement.WhileCarried => context.IsCarried,
            LightEmitterActivityRequirement.ActorTrait => context.IsActorTraitActive,
            _ => false,
        };
        var fuelSatisfied = definition.Activation.Fuel switch
        {
            LightEmitterFuelRequirement.None => true,
            LightEmitterFuelRequirement.WorkOrderInput => context.HasWorkOrderFuel,
            LightEmitterFuelRequirement.StoredFuel => context.HasStoredFuel,
            LightEmitterFuelRequirement.PortableCharge => context.HasPortableCharge,
            _ => false,
        };
        return activitySatisfied && fuelSatisfied;
    }

    public static bool IsStaticallyActive(LightEmitterDefinition definition) =>
        definition.Attachment == LightEmitterAttachment.World &&
        definition.Activation == new LightEmitterActivation(
            LightEmitterActivityRequirement.Always,
            LightEmitterFuelRequirement.None);

    public static bool IsValid(LightEmitterDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!Enum.IsDefined(definition.Activation.Activity) ||
            !Enum.IsDefined(definition.Activation.Fuel) ||
            !Enum.IsDefined(definition.Attachment))
        {
            return false;
        }

        if (definition.Activation.Fuel == LightEmitterFuelRequirement.WorkOrderInput &&
            definition.Activation.Activity != LightEmitterActivityRequirement.WhileWorking)
        {
            return false;
        }

        if (definition.Activation.Activity == LightEmitterActivityRequirement.WhileCarried ||
            definition.Activation.Fuel == LightEmitterFuelRequirement.PortableCharge)
        {
            return definition.Attachment == LightEmitterAttachment.Actor;
        }

        return definition.Attachment != LightEmitterAttachment.World ||
            definition.Activation.Activity != LightEmitterActivityRequirement.ActorTrait;
    }
}
