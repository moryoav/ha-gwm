# GWM Add-on

This add-on runs the native GWM bridge service used by the `gwm_ora` Home Assistant custom integration.

Configure the GWM cloud `region`, account registration country, and credentials in the add-on options before starting it. Use `eu` for Europe/Israel, `aus` for Australia/New Zealand, `rus` for the verified Russia integration, or `cn` for experimental mainland-China support. China uses the registered phone number in `username` and SMS login. China status reading supports NavInfo and BeanTech vehicles. BeanTech status was live-tested on a Tank 300 Hi4-T, while its initial remote commands are still unverified. See the [full add-on documentation](DOCS.md#mainland-china-cn-region-experimental) before testing China sensors or controls.

If GWM requests SMS/e-mail verification for the add-on device, enter the received code in `verification_code` and restart the add-on. Remote commands require `enable_remote_commands: true` and, outside China, the vehicle security PIN from the official app. The BeanTech PIN format is not implemented yet. Charging schedules use a separate `enable_charging_control: true` opt-in and do not require the PIN. China charging control currently supports NavInfo only.

The add-on exposes an authenticated Home Assistant Ingress status page through **Open Web UI**. Vehicle controls are provided by the native `gwm_ora` Home Assistant integration, including a 5-to-30-minute climate run-time setting for the next A/C command. Model-specific status signals are optional, and missing values do not interrupt polling or other entities.

The add-on exposes only an internal Home Assistant API port and publishes Supervisor discovery for the custom integration. MQTT is not used.
