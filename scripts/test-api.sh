#!/bin/bash

# Andy RBAC API Test Script
# Tests all major API endpoints

# Don't exit on first error - we want to run all tests
set +e

API_URL="${API_URL:-http://localhost:5100}"
PASS_COUNT=0
FAIL_COUNT=0

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log_pass() {
    echo -e "${GREEN}✓ PASS${NC}: $1"
    ((PASS_COUNT++))
}

log_fail() {
    echo -e "${RED}✗ FAIL${NC}: $1"
    ((FAIL_COUNT++))
}

log_info() {
    echo -e "${YELLOW}→${NC} $1"
}

log_section() {
    echo ""
    echo -e "${BLUE}=== $1 ===${NC}"
}

test_endpoint() {
    local name="$1"
    local method="$2"
    local endpoint="$3"
    local data="$4"
    local expected_status="$5"
    local show_body="${6:-true}"

    if [ -n "$data" ]; then
        response=$(curl -s -w "\n%{http_code}" -X "$method" "$API_URL$endpoint" \
            -H "Content-Type: application/json" \
            -d "$data" 2>/dev/null)
    else
        response=$(curl -s -w "\n%{http_code}" -X "$method" "$API_URL$endpoint" 2>/dev/null)
    fi

    status_code=$(echo "$response" | tail -n 1)
    body=$(echo "$response" | sed '$d')

    if [ "$status_code" == "$expected_status" ]; then
        log_pass "$name (HTTP $status_code)"
        if [ "$show_body" == "true" ] && [ -n "$body" ]; then
            echo "$body" | head -c 500
            if [ ${#body} -gt 500 ]; then echo "..."; fi
            echo ""
        fi
    else
        log_fail "$name (Expected $expected_status, got $status_code)"
        echo "$body"
    fi
}

echo "=============================================="
echo "       Andy RBAC API Test Suite"
echo "=============================================="
echo "API URL: $API_URL"
echo "Date: $(date)"
echo ""

# ============================================
# Health Check
# ============================================
log_section "Health Check"
test_endpoint "Health endpoint" "GET" "/health" "" "200"

# ============================================
# Applications
# ============================================
log_section "Applications"

log_info "Listing all applications..."
test_endpoint "List applications" "GET" "/api/applications" "" "200"

log_info "Getting andy-docs application by code..."
test_endpoint "Get application by code" "GET" "/api/applications/by-code/andy-docs" "" "200"

log_info "Creating a new test application..."
test_endpoint "Create application" "POST" "/api/applications" \
    '{"code":"test-app-'$$'","name":"Test Application","description":"Created by test script"}' "201"

# Store the created app ID for later tests
APP_CODE="test-app-$$"
APP_RESPONSE=$(curl -s "$API_URL/api/applications/by-code/$APP_CODE" 2>/dev/null)
TEST_APP_ID=$(echo "$APP_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
log_info "Created application ID: $TEST_APP_ID"

if [ -n "$TEST_APP_ID" ]; then
    log_info "Adding a resource type to test application..."
    test_endpoint "Add resource type" "POST" "/api/applications/$TEST_APP_ID/resource-types" \
        '{"code":"document","name":"Document","supportsInstances":true}' "201"
fi

# ============================================
# Roles
# ============================================
log_section "Roles"

log_info "Listing all roles..."
test_endpoint "List roles" "GET" "/api/roles" "" "200"

log_info "Listing roles for andy-docs..."
test_endpoint "List roles by app" "GET" "/api/roles?applicationCode=andy-docs" "" "200"

ROLE_CODE="test-role-$$"
log_info "Creating a new test role..."
test_endpoint "Create role" "POST" "/api/roles" \
    '{"code":"'"$ROLE_CODE"'","name":"Test Role","description":"Created by test script","applicationCode":"'"$APP_CODE"'"}' "201"

# Get role ID
ROLE_RESPONSE=$(curl -s "$API_URL/api/roles/by-code/$ROLE_CODE?applicationCode=$APP_CODE" 2>/dev/null)
TEST_ROLE_ID=$(echo "$ROLE_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
log_info "Created role ID: $TEST_ROLE_ID"

# ============================================
# Subjects
# ============================================
log_section "Subjects"

SUBJECT_ID="test-user-$$"
log_info "Creating a test user..."
test_endpoint "Create/Provision subject" "POST" "/api/subjects" \
    '{"externalId":"'"$SUBJECT_ID"'","provider":"andy-auth","email":"test-'$$'@example.com","displayName":"Test User"}' "201"

log_info "Searching for users..."
test_endpoint "Search users" "GET" "/api/subjects?query=test" "" "200"

log_info "Getting user by external ID..."
test_endpoint "Get user by external ID" "GET" "/api/subjects/by-external/andy-auth/$SUBJECT_ID" "" "200"

# Get subject internal ID
SUBJECT_RESPONSE=$(curl -s "$API_URL/api/subjects/by-external/andy-auth/$SUBJECT_ID" 2>/dev/null)
INTERNAL_SUBJECT_ID=$(echo "$SUBJECT_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
log_info "Subject internal ID: $INTERNAL_SUBJECT_ID"

# ============================================
# Role Assignments (via Subjects)
# ============================================
log_section "Role Assignments"

if [ -n "$INTERNAL_SUBJECT_ID" ]; then
    log_info "Assigning role to user..."
    # Note: This endpoint returns 201 Created on success
    test_endpoint "Assign role to user" "POST" "/api/subjects/$INTERNAL_SUBJECT_ID/roles" \
        '{"roleCode":"'"$ROLE_CODE"'"}' "201"

    log_info "Verifying user has role..."
    test_endpoint "Get user with roles" "GET" "/api/subjects/$INTERNAL_SUBJECT_ID" "" "200"

    log_info "Revoking role from user..."
    test_endpoint "Revoke role from user" "DELETE" "/api/subjects/$INTERNAL_SUBJECT_ID/roles/$ROLE_CODE" "" "204" "false"
fi

# ============================================
# Teams
# ============================================
log_section "Teams"

TEAM_CODE="test-team-$$"
log_info "Creating a test team..."
test_endpoint "Create team" "POST" "/api/teams" \
    '{"code":"'"$TEAM_CODE"'","name":"Test Team","description":"Created by test script"}' "201"

log_info "Listing all teams..."
test_endpoint "List teams" "GET" "/api/teams" "" "200"

# Get team ID
TEAM_RESPONSE=$(curl -s "$API_URL/api/teams/by-code/$TEAM_CODE" 2>/dev/null)
TEST_TEAM_ID=$(echo "$TEAM_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
log_info "Team ID: $TEST_TEAM_ID"

if [ -n "$TEST_TEAM_ID" ] && [ -n "$INTERNAL_SUBJECT_ID" ]; then
    log_info "Adding user to team..."
    # membershipRole is an enum: 0=Member, 1=Admin, 2=Owner
    test_endpoint "Add user to team" "POST" "/api/teams/$TEST_TEAM_ID/members" \
        '{"subjectExternalId":"'"$SUBJECT_ID"'","subjectProvider":"andy-auth","membershipRole":0}' "201"

    log_info "Getting team details with members..."
    test_endpoint "Get team" "GET" "/api/teams/$TEST_TEAM_ID" "" "200"

    log_info "Assigning role to team..."
    test_endpoint "Assign role to team" "POST" "/api/teams/$TEST_TEAM_ID/roles" \
        '{"roleCode":"'"$ROLE_CODE"'"}' "201"
fi

# ============================================
# Permission Checks
# ============================================
log_section "Permission Checks"

log_info "Checking permission (via Check API)..."
test_endpoint "Check permission" "POST" "/api/check" \
    '{"subjectId":"'"$SUBJECT_ID"'","permission":"andy-docs:document:read"}' "200"

log_info "Getting user permissions..."
test_endpoint "Get permissions" "GET" "/api/check/permissions/$SUBJECT_ID" "" "200"

log_info "Getting user roles..."
test_endpoint "Get roles" "GET" "/api/check/roles/$SUBJECT_ID" "" "200"

# ============================================
# Cleanup
# ============================================
log_section "Cleanup"

if [ -n "$TEST_TEAM_ID" ]; then
    # Remove team roles first
    log_info "Removing role from team..."
    curl -s -X DELETE "$API_URL/api/teams/$TEST_TEAM_ID/roles/$ROLE_CODE" > /dev/null 2>&1 || true

    # Remove team members
    if [ -n "$INTERNAL_SUBJECT_ID" ]; then
        log_info "Removing member from team..."
        curl -s -X DELETE "$API_URL/api/teams/$TEST_TEAM_ID/members/$INTERNAL_SUBJECT_ID" > /dev/null 2>&1 || true
    fi
fi

# Note: We won't delete the test data in order to preserve it for manual inspection
# If you want to clean up, uncomment these lines:
# log_info "Deleting test team..."
# curl -s -X DELETE "$API_URL/api/teams/$TEST_TEAM_ID" > /dev/null 2>&1 || true
# log_info "Deleting test role..."
# curl -s -X DELETE "$API_URL/api/roles/$TEST_ROLE_ID" > /dev/null 2>&1 || true
# log_info "Deleting test application..."
# curl -s -X DELETE "$API_URL/api/applications/$TEST_APP_ID" > /dev/null 2>&1 || true

echo ""
log_info "Test data preserved for manual inspection:"
echo "  Application Code: $APP_CODE"
echo "  Role Code: $ROLE_CODE"
echo "  Team Code: $TEAM_CODE"
echo "  Subject ID: $SUBJECT_ID"

# ============================================
# Summary
# ============================================
echo ""
echo "=============================================="
echo "                  Summary"
echo "=============================================="
echo -e "Passed: ${GREEN}$PASS_COUNT${NC}"
echo -e "Failed: ${RED}$FAIL_COUNT${NC}"
echo ""

if [ $FAIL_COUNT -gt 0 ]; then
    echo -e "${RED}Some tests failed!${NC}"
    exit 1
else
    echo -e "${GREEN}All tests passed!${NC}"
    exit 0
fi
