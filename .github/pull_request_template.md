## Summary

Describe what this pull request changes and why.

## Type of Change

- [ ] Bug fix
- [ ] New feature
- [ ] Documentation update
- [ ] Security hardening
- [ ] Maintenance or refactoring
- [ ] Release preparation
- [ ] Other

## Affected Area

- [ ] Add-on service
- [ ] GWM API client
- [ ] Custom integration
- [ ] Config flow, reauth, or reconfigure
- [ ] Entities or device model
- [ ] Diagnostics or repairs
- [ ] Remote commands
- [ ] Add-on packaging or images
- [ ] Documentation

## Testing

Describe the testing you performed.

- [ ] Home Assistant loads or restarts without relevant errors.
- [ ] The `gwm_ora` integration can be set up, reloaded, or reconfigured when affected.
- [ ] The add-on starts when affected.
- [ ] Relevant entities, diagnostics, repairs, or remote commands were tested.
- [ ] `dotnet test` passes.
- [ ] `python -m ruff check gwm_client custom_components tests/python` passes.
- [ ] `python -m pytest tests/python` passes.
- [ ] Documentation-only change; no runtime testing needed.

## Security and Privacy

- [ ] This change does not add GWM credentials, add-on API tokens, refresh tokens, security PINs, VINs, precise locations, private URLs, logs, or personal Home Assistant configuration.
- [ ] I considered whether this affects authentication, token storage, diagnostics redaction, Supervisor discovery, add-on networking, or remote vehicle commands.
- [ ] I updated `SECURITY.md` or documentation if this changes security-sensitive behavior.

## Documentation and Release Notes

- [ ] I updated relevant documentation, examples, or release notes.
- [ ] I updated `CHANGELOG.md` for user-facing changes.
- [ ] Documentation is not needed for this change.

## Related Issues

Link any related issues here.
