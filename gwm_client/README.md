# GWM Client

I use this internal alpha package as the Home Assistant independent protocol boundary for the GWM integration. It provides typed async clients for the EU, Australia and New Zealand, Russia, and isolated mainland China cloud strategies.

The package requires Python 3.13 or newer plus `aiohttp`, `cryptography`, and `yarl`. It does not import Home Assistant. Home Assistant owns the client session and lifecycle when the integration uses it.

I have prepared version `0.1.0` for reproducible local builds, but I have not published it. The custom integration manifest still has no client requirement. Publication and activation need separate approval at the final cutover checkpoint.

Some protocol values were obtained through interoperability research on official GWM apps. The repository records their provenance and unresolved distribution conditions in [Third-Party and Protocol Material Notice](https://github.com/moryoav/ha-gwm/blob/feature/integration-only/THIRD_PARTY_NOTICES.md). I do not claim that the project MIT license grants rights in those materials.
