"""Home Assistant-independent building blocks for the GWM cloud client.

The package contains the offline Task 2 protocol primitives and a deliberately
disposable, reuse-only Task 3 live-read proof.  It remains independent of Home
Assistant; the production async transport begins in Task 4.
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
