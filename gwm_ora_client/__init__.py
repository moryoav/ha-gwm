"""Home Assistant-independent building blocks for the GWM cloud client.

Task 2 intentionally exposes only the offline signing, certificate, CSR, and
TLS primitives needed to prove that the add-on protocol can be ported.  No
network transport or Home Assistant runtime code belongs in this package yet.
"""

from .crypto import (
    GeneratedClientCertificateRequest,
    generate_client_certificate_request,
    load_certificate,
    recover_transformed_private_key,
)
from .signing import (
    ANZ_BT_AUTH,
    EU_BT_AUTH,
    EU_GWM_AUTH,
    RUSSIA_GWM_AUTH,
    SignedRequest,
    SigningProfile,
    sign_request,
)
from .tls import LEGACY_CIPHER_STRING, create_gwm_ssl_context

__all__ = [
    "ANZ_BT_AUTH",
    "EU_BT_AUTH",
    "EU_GWM_AUTH",
    "LEGACY_CIPHER_STRING",
    "RUSSIA_GWM_AUTH",
    "GeneratedClientCertificateRequest",
    "SignedRequest",
    "SigningProfile",
    "create_gwm_ssl_context",
    "generate_client_certificate_request",
    "load_certificate",
    "recover_transformed_private_key",
    "sign_request",
]
