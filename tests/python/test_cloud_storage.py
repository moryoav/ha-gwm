"""Durable direct-cloud state and restart-safe journal tests."""

from __future__ import annotations

import asyncio
import base64
import json
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path

import pytest

pytest.importorskip("homeassistant")

from homeassistant.core import HomeAssistant

from custom_components.gwm_ora.cloud_auth import (
    CloudAuthState,
    DirectCloudCredentials,
    direct_entry_data,
    direct_unique_id,
)
from custom_components.gwm_ora.cloud_storage import (
    async_remove_direct_cloud_state,
    direct_authentication_context_binding,
    direct_cloud_state_store,
)
from gwm_ora_client import (
    AnzAuthState,
    ChinaAuthState,
    EuAuthState,
    EuIssuedIdentity,
    RussiaAuthState,
)

_DEVICE_ID = "0123456789abcdef0123456789abcdef"
_NOW = datetime(2026, 8, 28, 18, 0, tzinfo=UTC)


def _credentials(
    region: str = "eu",
    *,
    password: str | None = "private-password",
) -> DirectCloudCredentials:
    countries = {"eu": "DE", "aus": "AU", "rus": "RU", "cn": "CN"}
    return DirectCloudCredentials(
        region,
        countries[region],
        "private-account",
        None if region == "cn" else password,
        _DEVICE_ID,
    )


def _state(credentials: DirectCloudCredentials) -> CloudAuthState:
    regional = credentials.client_credentials()
    if credentials.region == "eu":
        return replace(
            EuAuthState.for_credentials(regional),
            access_token="private-eu-access",
            refresh_token="private-eu-refresh",
            gw_id="private-eu-gw",
            bean_id="private-eu-bean",
            issued_identity=EuIssuedIdentity(
                certificate=base64.b64encode(b"synthetic-certificate").decode(),
                private_key=base64.b64encode(b"synthetic-private-key").decode(),
            ),
            verification_requested_at=_NOW,
        )
    if credentials.region == "aus":
        return replace(
            AnzAuthState.for_credentials(regional),
            access_token="private-anz-access",
            refresh_token="private-anz-refresh",
            verification_requested_at=_NOW,
        )
    if credentials.region == "rus":
        return replace(
            RussiaAuthState.for_credentials(regional),
            access_token="private-russia-access",
            refresh_token="private-russia-refresh",
            gw_id="private-russia-gw",
            bean_id="private-russia-bean",
            verification_requested_at=_NOW,
        )
    assert credentials.region == "cn"
    return replace(
        ChinaAuthState.for_credentials(regional),
        g_token="private-china-access",
        g_refresh_token="private-china-refresh",
        sso_token="private-china-sso",
        pt_token="private-china-pt",
        user_id="private-china-user",
        bean_id="private-china-bean",
        verification_requested_at=_NOW,
    )


@pytest.mark.asyncio
@pytest.mark.parametrize("region", ["eu", "aus", "rus", "cn"])
async def test_regional_auth_state_survives_a_process_restart(
    tmp_path: Path,
    region: str,
) -> None:
    credentials = _credentials(region)
    unique_id = direct_unique_id(credentials)
    first_hass = HomeAssistant(str(tmp_path))
    first = direct_cloud_state_store(first_hass, unique_id)

    await first.async_save_auth_state(credentials, _state(credentials))

    second_hass = HomeAssistant(str(tmp_path))
    restored = await direct_cloud_state_store(
        second_hass,
        unique_id,
    ).async_load_auth_state(direct_entry_data(credentials))

    assert restored == _state(credentials)
    assert "private" not in repr(restored)
    if isinstance(restored, ChinaAuthState):
        assert restored.has_g_app
        assert not restored.complete


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "changed",
    [
        _credentials(password="replacement-password"),
        DirectCloudCredentials(
            "eu",
            "FR",
            "private-account",
            "private-password",
            _DEVICE_ID,
        ),
        DirectCloudCredentials(
            "eu",
            "DE",
            "replacement-account",
            "private-password",
            _DEVICE_ID,
        ),
        DirectCloudCredentials(
            "aus",
            "AU",
            "private-account",
            "private-password",
            _DEVICE_ID,
        ),
    ],
)
async def test_account_context_change_atomically_retires_state_and_commands(
    tmp_path: Path,
    changed: DirectCloudCredentials,
) -> None:
    credentials = _credentials()
    unique_id = direct_unique_id(credentials)
    hass = HomeAssistant(str(tmp_path))
    store = direct_cloud_state_store(hass, unique_id)
    await store.async_save_auth_state(credentials, _state(credentials))
    await store.async_record_accepted_command(
        credentials,
        vehicle_id="SYNTHETIC-VEHICLE",
        command_name="climate",
        cloud_command_id="SYNTHETIC-CLOUD-COMMAND",
        accepted_at=_NOW,
    )

    assert direct_authentication_context_binding(changed) != (
        direct_authentication_context_binding(credentials)
    )
    assert await store.async_load_auth_state(direct_entry_data(changed)) is None
    assert await store.async_get_command_journal(direct_entry_data(changed)) == ()

    stored_text = next((tmp_path / ".storage").glob("gwm_ora.direct_cloud.*")).read_text()
    assert "private-eu-access" not in stored_text
    assert "SYNTHETIC-CLOUD-COMMAND" not in stored_text
    assert "private-account" not in stored_text
    assert "replacement-password" not in stored_text
    assert "replacement-account" not in stored_text


@pytest.mark.asyncio
async def test_accepted_commands_are_serialized_and_restart_safe(
    tmp_path: Path,
) -> None:
    credentials = _credentials("aus")
    unique_id = direct_unique_id(credentials)
    first_hass = HomeAssistant(str(tmp_path))
    first = direct_cloud_state_store(first_hass, unique_id)
    await first.async_save_auth_state(credentials, _state(credentials))

    accepted = await asyncio.gather(
        *(
            first.async_record_accepted_command(
                credentials,
                vehicle_id="SYNTHETIC-VEHICLE",
                command_name="climate",
                cloud_command_id=f"SYNTHETIC-CLOUD-{index}",
                accepted_at=_NOW + timedelta(seconds=index),
            )
            for index in range(105)
        )
    )
    assert len({entry.journal_id for entry in accepted}) == 105
    assert all(entry.cloud_command_id not in repr(entry) for entry in accepted)
    assert "private" not in repr(first)

    second_hass = HomeAssistant(str(tmp_path))
    second = direct_cloud_state_store(second_hass, unique_id)
    restored = await second.async_get_command_journal(direct_entry_data(credentials))
    assert len(restored) == 100
    assert {entry.cloud_command_id for entry in restored} <= {
        f"SYNTHETIC-CLOUD-{index}" for index in range(105)
    }

    updated = await second.async_update_command(
        credentials,
        restored[0].journal_id,
        state="polling",
        updated_at=_NOW + timedelta(minutes=1),
    )
    assert updated.state == "polling"

    completed = await second.async_update_command(
        credentials,
        restored[0].journal_id,
        state="completed",
        updated_at=_NOW + timedelta(minutes=2),
    )
    with pytest.raises(ValueError):
        await second.async_update_command(
            credentials,
            completed.journal_id,
            state="polling",
            updated_at=_NOW + timedelta(minutes=3),
        )

    third_hass = HomeAssistant(str(tmp_path))
    after_update = await direct_cloud_state_store(
        third_hass,
        unique_id,
    ).async_get_command_journal(direct_entry_data(credentials))
    assert after_update[0].state == "completed"


@pytest.mark.asyncio
async def test_config_entry_removal_deletes_private_state(
    tmp_path: Path,
) -> None:
    credentials = _credentials("rus")
    unique_id = direct_unique_id(credentials)
    hass = HomeAssistant(str(tmp_path))
    await direct_cloud_state_store(hass, unique_id).async_save_auth_state(
        credentials,
        _state(credentials),
    )
    paths = list((tmp_path / ".storage").glob("gwm_ora.direct_cloud.*"))
    assert len(paths) == 1

    await async_remove_direct_cloud_state(hass, unique_id)

    assert not paths[0].exists()


@pytest.mark.asyncio
async def test_semantically_invalid_storage_fails_closed_and_is_overwritten(
    tmp_path: Path,
) -> None:
    credentials = _credentials()
    unique_id = direct_unique_id(credentials)
    first_hass = HomeAssistant(str(tmp_path))
    await direct_cloud_state_store(first_hass, unique_id).async_save_auth_state(
        credentials,
        _state(credentials),
    )
    path = next((tmp_path / ".storage").glob("gwm_ora.direct_cloud.*"))
    document = json.loads(path.read_text())
    document["data"]["auth_state"]["unexpected_secret"] = "must-be-removed"
    path.write_text(json.dumps(document))

    second_hass = HomeAssistant(str(tmp_path))
    restored = await direct_cloud_state_store(
        second_hass,
        unique_id,
    ).async_load_auth_state(direct_entry_data(credentials))

    assert restored is None
    rewritten = path.read_text()
    assert "must-be-removed" not in rewritten
    assert "private-eu-access" not in rewritten
