GO for Stage 2, with these precisions:

1. Lookup: a returned ticket counts as a match only if its Title contains
   the exact Poster ID, checked by the library. SearchText results alone
   are never treated as a match.
2. Requestor: send AccountID from the settings file. Do not add any
   current-user or other operation outside contract §12. If the sandbox
   rejects the creation for a missing RequestorUid, record it as a
   blocker in the report; do not work around it.
3. Auth: send the OpenAPI LoginParameters form (UserName, Password).
   Accept the token from either documented response form (JSON string or
   text/plain). The report records which form was received.
4. HTTP 429 is an error and is never retried outside the bounded lookup
   schedule:
   - during the pre-creation lookup, it blocks the creation (contract §9);
   - after an uncertain creation, it never leads to a new creation, and
     the outcome stays Uncertain.

All other Stage 1b findings stay sandbox findings for the validator's
reports.

Do not compile, run tests, publish, run the validator or commit.
Do not integrate TDX into PosterPrintRequest.Web.
