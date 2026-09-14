export { activeOperationStorageKeys, readStoredOperationId, storeOperationId, clearStoredOperationId } from "./utils";

export function readOperationQuery(searchParams: URLSearchParams) {
  return {
    installation: searchParams.get("operation"),
    setup: searchParams.get("setupOperation"),
    initialization: searchParams.get("initializeOperation"),
  };
}
