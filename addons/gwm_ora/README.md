# GWM ORA Add-on

This add-on runs the native GWM ORA bridge service used by the `gwm_ora` Home Assistant custom integration.

Configure the GWM cloud `region`, account registration country, e-mail, and password in the add-on options before starting it. Use `eu` for Europe/Israel or `aus` for Australia/New Zealand. If GWM requests SMS/e-mail verification for the add-on device, enter the received code in `verification_code` and restart the add-on. Remote commands require both `enable_remote_commands: true` and the vehicle security PIN from the official app. AU/NZ remote commands remain available but are currently experimental and unconfirmed; see the full documentation before testing them.

The add-on exposes an authenticated Home Assistant Ingress status page through **Open Web UI**. Vehicle controls are provided by the native `gwm_ora` Home Assistant integration, including a 5-to-30-minute climate run-time setting for the next A/C command. Model-specific status signals are optional, and missing values do not interrupt polling or other entities.

The add-on exposes only an internal Home Assistant API port and publishes Supervisor discovery for the custom integration. MQTT is not used.
