using libgwmapi.DTO.Vehicle;

namespace GwmOra.Addon.RemoteCommands;

public static class RemoteCommandFactory
{
    public static ModifyVecicleRemoteCtl CreateClimateDefaults(
        string vin,
        int temperature,
        int operationTimeMinutes)
    {
        return new ModifyVecicleRemoteCtl
        {
            AirConditionerTemperature = temperature.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AirConditionerTime = (operationTimeMinutes * 60).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Vin = vin
        };
    }

    public static SendCmd CreateClimateCommand(
        string vin,
        string securityPassword,
        string switchOrder,
        int temperature,
        int operationTimeMinutes)
    {
        return new SendCmd
        {
            Instructions = new SendCmdInstruction
            {
                X04 = new Instruction0x04
                {
                    AirConditioner = new AirConditionerInstruction
                    {
                        OperationTime = operationTimeMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        SwitchOrder = switchOrder,
                        Temperature = temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }
                }
            },
            RemoteType = "0",
            SecurityPassword = securityPassword,
            Type = 2,
            Vin = vin
        };
    }

    public static SendCmd CreateLockCommand(string vin, string securityPassword, bool lockVehicle)
    {
        return new SendCmd
        {
            Instructions = new SendCmdInstruction
            {
                X05 = new Instruction0x05
                {
                    OperationTime = "0",
                    SwitchOrder = lockVehicle ? "2" : "1"
                }
            },
            RemoteType = "0",
            SecurityPassword = securityPassword,
            Type = 2,
            Vin = vin
        };
    }

    public static SendCmd CreateWindowCloseCommand(string vin, string securityPassword)
    {
        return new SendCmd
        {
            Instructions = new SendCmdInstruction
            {
                X08 = new Instruction0x08
                {
                    SwitchOrder = "0",
                    Window = new WindowInstruction
                    {
                        LeftFront = "0",
                        LeftBack = "0",
                        RightFront = "0",
                        RightBack = "0",
                        SkyLight = String.Empty
                    }
                }
            },
            RemoteType = "0",
            SecurityPassword = securityPassword,
            Type = 2,
            Vin = vin
        };
    }
}
