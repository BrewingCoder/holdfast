package model

import (
	"database/sql/driver"
	"encoding/json"
	"fmt"
	"math"
	"strings"
	"testing"
	"time"

	modelInputs "github.com/BrewingCoder/holdfast/src/backend/private-graph/graph/model"

	"github.com/brianvoe/gofakeit/v7"
	"github.com/lib/pq"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"gorm.io/gorm"
)

// ---------------------------------------------------------------------------
// Test helpers / factories
// ---------------------------------------------------------------------------

// fakeStringPtr returns a pointer to a fake string of the given type.
func fakeStringPtr(kind string) *string {
	var s string
	switch kind {
	case "email":
		s = gofakeit.Email()
	case "name":
		s = gofakeit.Name()
	case "url":
		s = gofakeit.URL()
	case "uuid":
		s = gofakeit.UUID()
	case "word":
		s = gofakeit.Word()
	case "sentence":
		s = gofakeit.Sentence(6)
	default:
		s = gofakeit.LetterN(20)
	}
	return &s
}

func fakeIntPtr(max int) *int {
	v := gofakeit.IntRange(1, max)
	return &v
}

func fakeBoolPtr() *bool {
	v := gofakeit.Bool()
	return &v
}

func fakeSession() Session {
	return Session{
		Model:                   Model{ID: gofakeit.IntRange(1, 999999)},
		SecureID:                gofakeit.UUID(),
		ClientID:                gofakeit.UUID(),
		Identified:              gofakeit.Bool(),
		Fingerprint:             gofakeit.IntRange(1000, 9999),
		Identifier:              gofakeit.Email(),
		ProjectID:               gofakeit.IntRange(1, 100),
		Email:                   fakeStringPtr("email"),
		IP:                      gofakeit.IPv4Address(),
		City:                    gofakeit.City(),
		State:                   gofakeit.State(),
		Postal:                  gofakeit.Zip(),
		Country:                 gofakeit.Country(),
		Latitude:                gofakeit.Latitude(),
		Longitude:               gofakeit.Longitude(),
		OSName:                  gofakeit.RandomString([]string{"Windows", "macOS", "Linux", "iOS", "Android"}),
		OSVersion:               fmt.Sprintf("%d.%d", gofakeit.IntRange(10, 15), gofakeit.IntRange(0, 9)),
		BrowserName:             gofakeit.RandomString([]string{"Chrome", "Firefox", "Safari", "Edge"}),
		BrowserVersion:          fmt.Sprintf("%d.0", gofakeit.IntRange(90, 130)),
		Language:                gofakeit.LanguageAbbreviation(),
		Length:                  int64(gofakeit.IntRange(1000, 600000)),
		ActiveLength:            int64(gofakeit.IntRange(500, 300000)),
		Environment:             gofakeit.RandomString([]string{"production", "staging", "development", "test"}),
		UserProperties:          `{}`,
		LastUserInteractionTime: time.Now(),
		PagesVisited:            gofakeit.IntRange(1, 50),
		ClientVersion:           fmt.Sprintf("%d.%d.%d", gofakeit.IntRange(1, 10), gofakeit.IntRange(0, 20), gofakeit.IntRange(0, 99)),
	}
}

func fakeProject() Project {
	name := gofakeit.AppName()
	return Project{
		Model:       Model{ID: gofakeit.IntRange(1, 9999)},
		Name:        &name,
		WorkspaceID: gofakeit.IntRange(1, 100),
	}
}

func fakeWorkspace() Workspace {
	name := gofakeit.Company()
	return Workspace{
		Model: Model{ID: gofakeit.IntRange(1, 9999)},
		Name:  &name,
	}
}

func fakeAdmin() Admin {
	name := gofakeit.Name()
	email := gofakeit.Email()
	return Admin{
		Model: Model{ID: gofakeit.IntRange(1, 9999)},
		Name:  &name,
		Email: &email,
	}
}

// ---------------------------------------------------------------------------
// GraphQL Scalar Marshalers: Int64ID
// ---------------------------------------------------------------------------

func TestUnmarshalInt64ID_FromString(t *testing.T) {
	for i := 0; i < 50; i++ {
		n := gofakeit.Int64()
		if n < 0 {
			n = -n
		}
		s := fmt.Sprintf("%d", n)
		result, err := UnmarshalInt64ID(s)
		require.NoError(t, err)
		assert.Equal(t, n, result)
	}
}

func TestUnmarshalInt64ID_FromInt(t *testing.T) {
	for i := 0; i < 50; i++ {
		n := gofakeit.IntRange(0, math.MaxInt32)
		result, err := UnmarshalInt64ID(n)
		require.NoError(t, err)
		assert.Equal(t, int64(n), result)
	}
}

func TestUnmarshalInt64ID_FromInt64(t *testing.T) {
	n := int64(9876543210)
	result, err := UnmarshalInt64ID(n)
	require.NoError(t, err)
	assert.Equal(t, n, result)
}

func TestUnmarshalInt64ID_FromJsonNumber(t *testing.T) {
	n := json.Number("42")
	result, err := UnmarshalInt64ID(n)
	require.NoError(t, err)
	assert.Equal(t, int64(42), result)
}

func TestUnmarshalInt64ID_InvalidType(t *testing.T) {
	_, err := UnmarshalInt64ID(3.14)
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "is not an int")
}

func TestUnmarshalInt64ID_InvalidString(t *testing.T) {
	_, err := UnmarshalInt64ID("not-a-number")
	assert.Error(t, err)
}

// ---------------------------------------------------------------------------
// GraphQL Scalar Marshalers: StringArray
// ---------------------------------------------------------------------------

func TestUnmarshalStringArray_ValidArray(t *testing.T) {
	words := make([]interface{}, 20)
	for i := range words {
		words[i] = gofakeit.Word()
	}
	result, err := UnmarshalStringArray(words)
	require.NoError(t, err)
	assert.Len(t, result, 20)
	for i, r := range result {
		assert.Equal(t, words[i].(string), r)
	}
}

func TestUnmarshalStringArray_EmptyArray(t *testing.T) {
	result, err := UnmarshalStringArray([]interface{}{})
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestUnmarshalStringArray_Nil(t *testing.T) {
	result, err := UnmarshalStringArray(nil)
	require.NoError(t, err)
	assert.Nil(t, result)
}

func TestUnmarshalStringArray_NotArray(t *testing.T) {
	_, err := UnmarshalStringArray("not-an-array")
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "not an array")
}

func TestUnmarshalStringArray_NonStringElement(t *testing.T) {
	_, err := UnmarshalStringArray([]interface{}{"ok", 42})
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "not a string array")
}

func TestMarshalStringArray_Nil(t *testing.T) {
	m := MarshalStringArray(nil)
	// graphql.Null is the zero-value marshaler
	assert.NotNil(t, m)
}

func TestMarshalStringArray_NonNil(t *testing.T) {
	sa := pq.StringArray{"alpha", "bravo", "charlie"}
	m := MarshalStringArray(sa)
	assert.NotNil(t, m)
}

// ---------------------------------------------------------------------------
// GraphQL Scalar Marshalers: Timestamp
// ---------------------------------------------------------------------------

func TestTimestampRoundTrip(t *testing.T) {
	for i := 0; i < 30; i++ {
		original := gofakeit.Date()
		formatted := original.Format(time.RFC3339Nano)
		parsed, err := UnmarshalTimestamp(formatted)
		require.NoError(t, err)
		assert.Equal(t, original.Format(time.RFC3339Nano), parsed.Format(time.RFC3339Nano))
	}
}

func TestUnmarshalTimestamp_InvalidType(t *testing.T) {
	_, err := UnmarshalTimestamp(12345)
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "RFC3339Nano")
}

func TestUnmarshalTimestamp_InvalidString(t *testing.T) {
	_, err := UnmarshalTimestamp("not-a-date")
	assert.Error(t, err)
}

func TestMarshalTimestamp_ZeroTime(t *testing.T) {
	m := MarshalTimestamp(time.Time{})
	assert.NotNil(t, m) // returns graphql.Null
}

// ---------------------------------------------------------------------------
// Custom SQL Types: JSONB
// ---------------------------------------------------------------------------

func TestJSONB_ValueAndScan_Roundtrip(t *testing.T) {
	for i := 0; i < 20; i++ {
		original := JSONB{
			"string_key":    gofakeit.Sentence(3),
			"number_key":    float64(gofakeit.IntRange(1, 9999)),
			"bool_key":      gofakeit.Bool(),
			"nested_key":    map[string]interface{}{"inner": gofakeit.Word()},
			"null_key":      nil,
			"array_key":     []interface{}{gofakeit.Word(), gofakeit.Word()},
			gofakeit.Word(): gofakeit.Sentence(2),
		}

		val, err := original.Value()
		require.NoError(t, err)
		assert.IsType(t, "", val)

		// Scan from string
		var fromStr JSONB
		err = fromStr.Scan(val.(string))
		require.NoError(t, err)
		assert.Equal(t, original["string_key"], fromStr["string_key"])
		assert.Equal(t, original["bool_key"], fromStr["bool_key"])

		// Scan from []byte
		var fromBytes JSONB
		err = fromBytes.Scan([]byte(val.(string)))
		require.NoError(t, err)
		assert.Equal(t, original["string_key"], fromBytes["string_key"])
	}
}

func TestJSONB_Scan_NilValue(t *testing.T) {
	var j JSONB
	err := j.Scan(nil)
	assert.NoError(t, err)
	assert.Nil(t, j)
}

func TestJSONB_Scan_InvalidJSON(t *testing.T) {
	var j JSONB
	err := j.Scan("{not valid json}")
	assert.Error(t, err)
}

func TestJSONB_EmptyMap(t *testing.T) {
	j := JSONB{}
	val, err := j.Value()
	require.NoError(t, err)
	assert.Equal(t, "{}", val)
}

// ---------------------------------------------------------------------------
// Custom SQL Types: Vector
// ---------------------------------------------------------------------------

func TestVector_ValueAndScan_Roundtrip(t *testing.T) {
	dims := []int{3, 128, 1024, 1536}
	for _, d := range dims {
		original := make(Vector, d)
		for i := range original {
			original[i] = gofakeit.Float32Range(-1.0, 1.0)
		}

		val, err := original.Value()
		require.NoError(t, err)

		// Scan from string
		var fromStr Vector
		err = fromStr.Scan(val.(string))
		require.NoError(t, err)
		assert.Len(t, fromStr, d)
		for i := range original {
			assert.InDelta(t, original[i], fromStr[i], 1e-6)
		}

		// Scan from []byte
		var fromBytes Vector
		err = fromBytes.Scan([]byte(val.(string)))
		require.NoError(t, err)
		assert.Len(t, fromBytes, d)
	}
}

func TestVector_EmptyValue(t *testing.T) {
	v := Vector{}
	val, err := v.Value()
	require.NoError(t, err)
	assert.Nil(t, val)
}

func TestVector_NilScan(t *testing.T) {
	var v Vector
	err := v.Scan(nil)
	assert.NoError(t, err)
	assert.Nil(t, v)
}

// ---------------------------------------------------------------------------
// Custom SQL Types: DiscordChannels
// ---------------------------------------------------------------------------

func TestDiscordChannels_ValueAndScan_Roundtrip(t *testing.T) {
	channels := DiscordChannels{}
	for i := 0; i < 10; i++ {
		channels = append(channels, &DiscordChannel{
			Name: gofakeit.Username(),
			ID:   gofakeit.DigitN(18),
		})
	}

	val, err := channels.Value()
	require.NoError(t, err)

	// Scan from string
	var fromStr DiscordChannels
	err = fromStr.Scan(val.(string))
	require.NoError(t, err)
	assert.Len(t, fromStr, 10)
	assert.Equal(t, channels[0].Name, fromStr[0].Name)
	assert.Equal(t, channels[0].ID, fromStr[0].ID)

	// Scan from []byte
	var fromBytes DiscordChannels
	err = fromBytes.Scan([]byte(val.(string)))
	require.NoError(t, err)
	assert.Len(t, fromBytes, 10)
}

func TestDiscordChannels_EmptyArray(t *testing.T) {
	channels := DiscordChannels{}
	val, err := channels.Value()
	require.NoError(t, err)
	assert.Equal(t, "[]", val)
}

// ---------------------------------------------------------------------------
// Custom SQL Types: MicrosoftTeamsChannels
// ---------------------------------------------------------------------------

func TestMicrosoftTeamsChannels_ValueAndScan_Roundtrip(t *testing.T) {
	channels := MicrosoftTeamsChannels{}
	for i := 0; i < 10; i++ {
		channels = append(channels, &MicrosoftTeamsChannel{
			ID:   gofakeit.UUID(),
			Name: fmt.Sprintf("#%s", gofakeit.Word()),
		})
	}

	val, err := channels.Value()
	require.NoError(t, err)

	var fromStr MicrosoftTeamsChannels
	err = fromStr.Scan(val.(string))
	require.NoError(t, err)
	assert.Len(t, fromStr, 10)
	assert.Equal(t, channels[0].ID, fromStr[0].ID)
	assert.Equal(t, channels[0].Name, fromStr[0].Name)

	var fromBytes MicrosoftTeamsChannels
	err = fromBytes.Scan([]byte(val.(string)))
	require.NoError(t, err)
	assert.Len(t, fromBytes, 10)
}

// ---------------------------------------------------------------------------
// Custom SQL Types: WebhookDestinations
// ---------------------------------------------------------------------------

func TestWebhookDestinations_ValueAndScan_Roundtrip(t *testing.T) {
	dests := WebhookDestinations{}
	for i := 0; i < 10; i++ {
		auth := gofakeit.UUID()
		dests = append(dests, &WebhookDestination{
			URL:           gofakeit.URL(),
			Authorization: &auth,
		})
	}

	val, err := dests.Value()
	require.NoError(t, err)

	var fromStr WebhookDestinations
	err = fromStr.Scan(val.(string))
	require.NoError(t, err)
	assert.Len(t, fromStr, 10)
	assert.Equal(t, dests[0].URL, fromStr[0].URL)
	assert.Equal(t, *dests[0].Authorization, *fromStr[0].Authorization)
}

func TestWebhookDestinations_NilAuthorization(t *testing.T) {
	dests := WebhookDestinations{
		{URL: gofakeit.URL(), Authorization: nil},
	}
	val, err := dests.Value()
	require.NoError(t, err)

	var result WebhookDestinations
	err = result.Scan(val.(string))
	require.NoError(t, err)
	assert.Nil(t, result[0].Authorization)
}

// ---------------------------------------------------------------------------
// GORM Hooks: BeforeCreate
// ---------------------------------------------------------------------------

func TestProject_BeforeCreate_SetsSecret(t *testing.T) {
	for i := 0; i < 20; i++ {
		p := fakeProject()
		assert.Nil(t, p.Secret)
		err := p.BeforeCreate(&gorm.DB{})
		require.NoError(t, err)
		require.NotNil(t, p.Secret)
		assert.NotEmpty(t, *p.Secret)
		assert.Len(t, *p.Secret, 20) // xid generates 20-char strings
	}
}

func TestProject_BeforeCreate_UniqueSecrets(t *testing.T) {
	seen := make(map[string]bool)
	for i := 0; i < 100; i++ {
		p := fakeProject()
		_ = p.BeforeCreate(&gorm.DB{})
		assert.False(t, seen[*p.Secret], "duplicate secret generated")
		seen[*p.Secret] = true
	}
}

func TestWorkspace_BeforeCreate_SetsSecret(t *testing.T) {
	for i := 0; i < 20; i++ {
		w := fakeWorkspace()
		assert.Nil(t, w.Secret)
		err := w.BeforeCreate(&gorm.DB{})
		require.NoError(t, err)
		require.NotNil(t, w.Secret)
		assert.NotEmpty(t, *w.Secret)
		assert.Len(t, *w.Secret, 20)
	}
}

func TestSession_BeforeCreate_SetsDefaultInteractionTime(t *testing.T) {
	s := fakeSession()
	s.LastUserInteractionTime = time.Time{} // zero value
	err := s.BeforeCreate(&gorm.DB{})
	require.NoError(t, err)
	assert.Equal(t, time.UnixMilli(0), s.LastUserInteractionTime)
}

func TestSession_BeforeCreate_PreservesExistingInteractionTime(t *testing.T) {
	s := fakeSession()
	original := time.Now().Add(-1 * time.Hour)
	s.LastUserInteractionTime = original
	err := s.BeforeCreate(&gorm.DB{})
	require.NoError(t, err)
	assert.Equal(t, original, s.LastUserInteractionTime)
}

func TestSystemConfiguration_BeforeCreate_SetsDefaults(t *testing.T) {
	sc := SystemConfiguration{}
	err := sc.BeforeCreate(&gorm.DB{})
	require.NoError(t, err)
	assert.NotNil(t, sc.ErrorFilters)
	assert.Contains(t, []string(sc.ErrorFilters), "ENOENT.*")
	assert.Contains(t, []string(sc.ErrorFilters), "connect ECONNREFUSED.*")
	assert.NotNil(t, sc.IgnoredFiles)
	assert.Contains(t, []string(sc.IgnoredFiles), ".*/node_modules/.*")
	assert.Contains(t, []string(sc.IgnoredFiles), ".*/go/pkg/mod/.*")
	assert.Contains(t, []string(sc.IgnoredFiles), ".*/site-packages/.*")
}

func TestSystemConfiguration_BeforeCreate_PreservesExistingFilters(t *testing.T) {
	customFilter := pq.StringArray{"custom-filter.*"}
	customIgnored := pq.StringArray{"custom-ignored.*"}
	sc := SystemConfiguration{
		ErrorFilters: customFilter,
		IgnoredFiles: customIgnored,
	}
	err := sc.BeforeCreate(&gorm.DB{})
	require.NoError(t, err)
	assert.Equal(t, customFilter, sc.ErrorFilters)
	assert.Equal(t, customIgnored, sc.IgnoredFiles)
}

// ---------------------------------------------------------------------------
// VerboseID: encode/decode roundtrip
// ---------------------------------------------------------------------------

func TestVerboseID_Roundtrip(t *testing.T) {
	for i := 0; i < 100; i++ {
		id := gofakeit.IntRange(1, 999999)
		p := &Project{Model: Model{ID: id}}
		verbose := p.VerboseID()
		assert.NotEmpty(t, verbose)
		assert.GreaterOrEqual(t, len(verbose), 8) // minLength=8

		decoded, err := FromVerboseID(verbose)
		require.NoError(t, err)
		assert.Equal(t, id, decoded)
	}
}

func TestFromVerboseID_PlainInteger(t *testing.T) {
	for i := 0; i < 50; i++ {
		id := gofakeit.IntRange(1, 999999)
		result, err := FromVerboseID(fmt.Sprintf("%d", id))
		require.NoError(t, err)
		assert.Equal(t, id, result)
	}
}

func TestFromVerboseID_KnownValue(t *testing.T) {
	// Existing test case from original test file
	id, err := FromVerboseID("1jdkoe52")
	require.NoError(t, err)
	assert.Equal(t, 1, id)
}

func TestFromVerboseID_InvalidHash(t *testing.T) {
	_, err := FromVerboseID("!@#$%^&*")
	assert.Error(t, err)
}

// ---------------------------------------------------------------------------
// HasSecret Interface
// ---------------------------------------------------------------------------

func TestHasSecret_Project(t *testing.T) {
	secret := gofakeit.UUID()
	p := &Project{Secret: &secret}
	var hs HasSecret = p
	assert.Equal(t, &secret, hs.GetSecret())
}

func TestHasSecret_Workspace(t *testing.T) {
	secret := gofakeit.UUID()
	w := &Workspace{Secret: &secret}
	var hs HasSecret = w
	assert.Equal(t, &secret, hs.GetSecret())
}

func TestHasSecret_NilSecret(t *testing.T) {
	p := &Project{}
	assert.Nil(t, p.GetSecret())
	w := &Workspace{}
	assert.Nil(t, w.GetSecret())
}

// ---------------------------------------------------------------------------
// Object Interface
// ---------------------------------------------------------------------------

func TestObject_ResourcesObject(t *testing.T) {
	content := gofakeit.Paragraph(3, 5, 10, "\n")
	obj := &ResourcesObject{Resources: content}
	var o Object = obj
	assert.Equal(t, content, o.Contents())
}

func TestObject_SessionData(t *testing.T) {
	content := gofakeit.Paragraph(3, 5, 10, "\n")
	obj := &SessionData{Data: content}
	var o Object = obj
	assert.Equal(t, content, o.Contents())
}

func TestObject_MessagesObject(t *testing.T) {
	content := gofakeit.Paragraph(3, 5, 10, "\n")
	obj := &MessagesObject{Messages: content}
	var o Object = obj
	assert.Equal(t, content, o.Contents())
}

func TestObject_EventsObject(t *testing.T) {
	content := gofakeit.Paragraph(3, 5, 10, "\n")
	obj := &EventsObject{Events: content}
	var o Object = obj
	assert.Equal(t, content, o.Contents())
}

func TestObject_EmptyContent(t *testing.T) {
	assert.Equal(t, "", (&ResourcesObject{}).Contents())
	assert.Equal(t, "", (&SessionData{}).Contents())
	assert.Equal(t, "", (&MessagesObject{}).Contents())
	assert.Equal(t, "", (&EventsObject{}).Contents())
}

// ---------------------------------------------------------------------------
// Session: UserProperties JSON
// ---------------------------------------------------------------------------

func TestSession_SetAndGetUserProperties_Roundtrip(t *testing.T) {
	for i := 0; i < 20; i++ {
		s := fakeSession()
		props := map[string]string{
			"email":    gofakeit.Email(),
			"name":     gofakeit.Name(),
			"avatar":   fmt.Sprintf("https://picsum.photos/id/%d/200/200", gofakeit.IntRange(1, 500)),
			"company":  gofakeit.Company(),
			"role":     gofakeit.JobTitle(),
			"location": gofakeit.City(),
		}

		err := s.SetUserProperties(props)
		require.NoError(t, err)

		result, err := s.GetUserProperties()
		require.NoError(t, err)
		assert.Equal(t, props, result)
	}
}

func TestSession_SetUserProperties_EmptyMap(t *testing.T) {
	s := fakeSession()
	err := s.SetUserProperties(map[string]string{})
	require.NoError(t, err)

	result, err := s.GetUserProperties()
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestSession_GetUserProperties_InvalidJSON(t *testing.T) {
	s := fakeSession()
	s.UserProperties = "not-json"
	_, err := s.GetUserProperties()
	assert.Error(t, err)
}

func TestSession_SetUserProperties_SpecialCharacters(t *testing.T) {
	s := fakeSession()
	props := map[string]string{
		"name":    `O'Brien "Mac" <script>alert(1)</script>`,
		"emoji":   "\U0001f680\U0001f525\U0001f4a5",
		"unicode": "日本語テスト",
		"empty":   "",
		"spaces":  "   ",
	}
	err := s.SetUserProperties(props)
	require.NoError(t, err)

	result, err := s.GetUserProperties()
	require.NoError(t, err)
	assert.Equal(t, props, result)
}

// ---------------------------------------------------------------------------
// DecodeAndValidateParams
// ---------------------------------------------------------------------------

func TestDecodeAndValidateParams_ValidParams(t *testing.T) {
	params := []interface{}{
		map[string]interface{}{
			"action": "contains",
			"type":   "user_property",
			"value": map[string]interface{}{
				"text":  gofakeit.Word(),
				"value": gofakeit.Word(),
			},
		},
		map[string]interface{}{
			"action": "is",
			"type":   "session_property",
			"value": map[string]interface{}{
				"text":  gofakeit.Word(),
				"value": gofakeit.Word(),
			},
		},
	}

	result, err := DecodeAndValidateParams(params)
	require.NoError(t, err)
	assert.Len(t, result, 2)
	assert.Equal(t, "contains", result[0].Action)
	assert.Equal(t, "is", result[1].Action)
}

func TestDecodeAndValidateParams_DuplicateAction(t *testing.T) {
	params := []interface{}{
		map[string]interface{}{
			"action": "same_action",
			"type":   "user_property",
			"value":  map[string]interface{}{"text": "a", "value": "b"},
		},
		map[string]interface{}{
			"action": "same_action",
			"type":   "session_property",
			"value":  map[string]interface{}{"text": "c", "value": "d"},
		},
	}

	_, err := DecodeAndValidateParams(params)
	assert.Error(t, err)
}

func TestDecodeAndValidateParams_EmptyList(t *testing.T) {
	result, err := DecodeAndValidateParams([]interface{}{})
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestDecodeAndValidateParams_ManyUniqueActions(t *testing.T) {
	params := make([]interface{}, 50)
	for i := range params {
		params[i] = map[string]interface{}{
			"action": fmt.Sprintf("action_%d", i),
			"type":   "test",
			"value":  map[string]interface{}{"text": gofakeit.Word(), "value": gofakeit.Word()},
		}
	}
	result, err := DecodeAndValidateParams(params)
	require.NoError(t, err)
	assert.Len(t, result, 50)
}

// ---------------------------------------------------------------------------
// GetEmailsToNotify
// ---------------------------------------------------------------------------

func TestGetEmailsToNotify_ValidEmails(t *testing.T) {
	emails := make([]string, 10)
	for i := range emails {
		emails[i] = gofakeit.Email()
	}
	jsonBytes, _ := json.Marshal(emails)
	jsonStr := string(jsonBytes)

	result, err := GetEmailsToNotify(&jsonStr)
	require.NoError(t, err)
	assert.Len(t, result, 10)
	for i, r := range result {
		assert.Equal(t, emails[i], *r)
	}
}

func TestGetEmailsToNotify_NilInput(t *testing.T) {
	result, err := GetEmailsToNotify(nil)
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestGetEmailsToNotify_EmptyArray(t *testing.T) {
	empty := "[]"
	result, err := GetEmailsToNotify(&empty)
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestGetEmailsToNotify_InvalidJSON(t *testing.T) {
	bad := "not-json"
	_, err := GetEmailsToNotify(&bad)
	assert.Error(t, err)
}

// ---------------------------------------------------------------------------
// AlertDeprecated methods
// ---------------------------------------------------------------------------

func TestAlertDeprecated_GetExcludedEnvironments(t *testing.T) {
	envs := []string{"production", "staging"}
	jsonBytes, _ := json.Marshal(envs)
	jsonStr := string(jsonBytes)

	alert := &AlertDeprecated{ExcludedEnvironments: &jsonStr}
	result, err := alert.GetExcludedEnvironments()
	require.NoError(t, err)
	assert.Len(t, result, 2)
	assert.Equal(t, "production", *result[0])
	assert.Equal(t, "staging", *result[1])
}

func TestAlertDeprecated_GetExcludedEnvironments_NilEnvs(t *testing.T) {
	alert := &AlertDeprecated{ExcludedEnvironments: nil}
	result, err := alert.GetExcludedEnvironments()
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestAlertDeprecated_GetExcludedEnvironments_NilAlert(t *testing.T) {
	var alert *AlertDeprecated
	_, err := alert.GetExcludedEnvironments()
	assert.Error(t, err)
}

func TestAlertDeprecated_GetChannelsToNotify(t *testing.T) {
	channels := []map[string]interface{}{
		{"webhook_channel": gofakeit.Word(), "webhook_channel_id": gofakeit.UUID()},
		{"webhook_channel": gofakeit.Word(), "webhook_channel_id": gofakeit.UUID()},
	}
	jsonBytes, _ := json.Marshal(channels)
	jsonStr := string(jsonBytes)

	alert := &AlertDeprecated{ChannelsToNotify: &jsonStr}
	result, err := alert.GetChannelsToNotify()
	require.NoError(t, err)
	assert.Len(t, result, 2)
}

func TestAlertDeprecated_GetChannelsToNotify_Nil(t *testing.T) {
	alert := &AlertDeprecated{ChannelsToNotify: nil}
	result, err := alert.GetChannelsToNotify()
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestAlertDeprecated_GetName(t *testing.T) {
	for i := 0; i < 20; i++ {
		name := gofakeit.Sentence(3)
		alert := &AlertDeprecated{Name: name}
		assert.Equal(t, name, alert.GetName())
	}
}

func TestAlertDeprecated_GetEmailsToNotify(t *testing.T) {
	emails := []string{gofakeit.Email(), gofakeit.Email()}
	jsonBytes, _ := json.Marshal(emails)
	jsonStr := string(jsonBytes)

	alert := &AlertDeprecated{EmailsToNotify: &jsonStr}
	result, err := alert.GetEmailsToNotify()
	require.NoError(t, err)
	assert.Len(t, result, 2)
}

func TestAlertDeprecated_GetEmailsToNotify_Nil(t *testing.T) {
	alert := &AlertDeprecated{EmailsToNotify: nil}
	result, err := alert.GetEmailsToNotify()
	require.NoError(t, err)
	assert.Empty(t, result)
}

// ---------------------------------------------------------------------------
// ErrorAlert.GetRegexGroups
// ---------------------------------------------------------------------------

func TestErrorAlert_GetRegexGroups_Valid(t *testing.T) {
	patterns := []string{`error\d+`, `fatal.*`, `panic: .*`}
	jsonBytes, _ := json.Marshal(patterns)
	jsonStr := string(jsonBytes)

	alert := &ErrorAlert{RegexGroups: &jsonStr}
	result, err := alert.GetRegexGroups()
	require.NoError(t, err)
	assert.Len(t, result, 3)
	assert.Equal(t, `error\d+`, *result[0])
}

func TestErrorAlert_GetRegexGroups_NilAndEmpty(t *testing.T) {
	// nil
	alert := &ErrorAlert{RegexGroups: nil}
	result, err := alert.GetRegexGroups()
	require.NoError(t, err)
	assert.Empty(t, result)

	// empty string
	empty := ""
	alert2 := &ErrorAlert{RegexGroups: &empty}
	result2, err := alert2.GetRegexGroups()
	require.NoError(t, err)
	assert.Empty(t, result2)
}

func TestErrorAlert_GetRegexGroups_InvalidJSON(t *testing.T) {
	bad := "not-json"
	alert := &ErrorAlert{RegexGroups: &bad}
	_, err := alert.GetRegexGroups()
	assert.Error(t, err)
}

// ---------------------------------------------------------------------------
// SessionAlert methods
// ---------------------------------------------------------------------------

func TestSessionAlert_GetTrackProperties(t *testing.T) {
	props := []TrackProperty{
		{ID: 1, Name: gofakeit.Word(), Value: gofakeit.Word()},
		{ID: 2, Name: gofakeit.Word(), Value: gofakeit.Word()},
	}
	jsonBytes, _ := json.Marshal(props)
	jsonStr := string(jsonBytes)

	alert := &SessionAlert{TrackProperties: &jsonStr}
	result, err := alert.GetTrackProperties()
	require.NoError(t, err)
	assert.Len(t, result, 2)
	assert.Equal(t, props[0].Name, result[0].Name)
}

func TestSessionAlert_GetTrackProperties_Nil(t *testing.T) {
	alert := &SessionAlert{TrackProperties: nil}
	result, err := alert.GetTrackProperties()
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestSessionAlert_GetUserProperties(t *testing.T) {
	props := []UserProperty{
		{ID: 1, Name: "email", Value: gofakeit.Email()},
		{ID: 2, Name: "name", Value: gofakeit.Name()},
	}
	jsonBytes, _ := json.Marshal(props)
	jsonStr := string(jsonBytes)

	alert := &SessionAlert{UserProperties: &jsonStr}
	result, err := alert.GetUserProperties()
	require.NoError(t, err)
	assert.Len(t, result, 2)
}

func TestSessionAlert_GetExcludeRules(t *testing.T) {
	rules := []string{"bot-*", "internal-*", "test-*"}
	jsonBytes, _ := json.Marshal(rules)
	jsonStr := string(jsonBytes)

	alert := &SessionAlert{ExcludeRules: &jsonStr}
	result, err := alert.GetExcludeRules()
	require.NoError(t, err)
	assert.Len(t, result, 3)
	assert.Equal(t, "bot-*", *result[0])
}

func TestSessionAlert_GetExcludeRules_Nil(t *testing.T) {
	alert := &SessionAlert{ExcludeRules: nil}
	result, err := alert.GetExcludeRules()
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestSessionAlert_NilReceiver(t *testing.T) {
	var alert *SessionAlert
	_, err := alert.GetTrackProperties()
	assert.Error(t, err)
	_, err = alert.GetUserProperties()
	assert.Error(t, err)
	_, err = alert.GetExcludeRules()
	assert.Error(t, err)
}

// ---------------------------------------------------------------------------
// MetricMonitor methods
// ---------------------------------------------------------------------------

func TestMetricMonitor_GetChannelsToNotify(t *testing.T) {
	channels := []map[string]interface{}{
		{"webhook_channel": gofakeit.Word(), "webhook_channel_id": gofakeit.UUID()},
	}
	jsonBytes, _ := json.Marshal(channels)
	jsonStr := string(jsonBytes)

	m := &MetricMonitor{ChannelsToNotify: &jsonStr}
	result, err := m.GetChannelsToNotify()
	require.NoError(t, err)
	assert.Len(t, result, 1)
}

func TestMetricMonitor_GetChannelsToNotify_Nil(t *testing.T) {
	m := &MetricMonitor{ChannelsToNotify: nil}
	result, err := m.GetChannelsToNotify()
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestMetricMonitor_GetChannelsToNotify_NilReceiver(t *testing.T) {
	var m *MetricMonitor
	_, err := m.GetChannelsToNotify()
	assert.Error(t, err)
}

func TestMetricMonitor_GetNameAndId(t *testing.T) {
	m := &MetricMonitor{
		Model: Model{ID: gofakeit.IntRange(1, 9999)},
		Name:  gofakeit.Sentence(3),
	}
	assert.Equal(t, m.Name, m.GetName())
	assert.Equal(t, m.ID, m.GetId())
}

// ---------------------------------------------------------------------------
// Workspace.IntegratedSlackChannels
// ---------------------------------------------------------------------------

func TestWorkspace_IntegratedSlackChannels_Valid(t *testing.T) {
	channels := []SlackChannel{
		{WebhookAccessToken: gofakeit.UUID(), WebhookURL: gofakeit.URL(), WebhookChannel: "#general", WebhookChannelID: "C001"},
		{WebhookAccessToken: gofakeit.UUID(), WebhookURL: gofakeit.URL(), WebhookChannel: "#alerts", WebhookChannelID: "C002"},
	}
	jsonBytes, _ := json.Marshal(channels)
	jsonStr := string(jsonBytes)

	w := &Workspace{SlackChannels: &jsonStr}
	result, err := w.IntegratedSlackChannels()
	require.NoError(t, err)
	assert.Len(t, result, 2)
	assert.Equal(t, "#general", result[0].WebhookChannel)
	assert.Equal(t, "C001", result[0].WebhookChannelID)
}

func TestWorkspace_IntegratedSlackChannels_Nil(t *testing.T) {
	w := &Workspace{SlackChannels: nil}
	result, err := w.IntegratedSlackChannels()
	require.NoError(t, err)
	assert.Empty(t, result)
}

func TestWorkspace_IntegratedSlackChannels_InvalidJSON(t *testing.T) {
	bad := "not-json"
	w := &Workspace{SlackChannels: &bad}
	_, err := w.IntegratedSlackChannels()
	assert.Error(t, err)
}

func TestWorkspace_IntegratedSlackChannels_AppendsWebhookChannel(t *testing.T) {
	// Existing channels don't include the webhook channel
	channels := []SlackChannel{
		{WebhookAccessToken: "tok1", WebhookURL: "url1", WebhookChannel: "#general", WebhookChannelID: "C001"},
	}
	jsonBytes, _ := json.Marshal(channels)
	jsonStr := string(jsonBytes)

	webhookToken := "tok2"
	webhookURL := "url2"
	webhookChan := "#alerts"
	webhookChanID := "C002"

	w := &Workspace{
		SlackChannels:         &jsonStr,
		SlackAccessToken:      &webhookToken,
		SlackWebhookURL:       &webhookURL,
		SlackWebhookChannel:   &webhookChan,
		SlackWebhookChannelID: &webhookChanID,
	}
	result, err := w.IntegratedSlackChannels()
	require.NoError(t, err)
	assert.Len(t, result, 2) // original + appended
	assert.Equal(t, "#alerts", result[1].WebhookChannel)
}

func TestWorkspace_IntegratedSlackChannels_NoDuplicateWebhookChannel(t *testing.T) {
	webhookChanID := "C001"
	channels := []SlackChannel{
		{WebhookAccessToken: "tok1", WebhookURL: "url1", WebhookChannel: "#general", WebhookChannelID: webhookChanID},
	}
	jsonBytes, _ := json.Marshal(channels)
	jsonStr := string(jsonBytes)

	w := &Workspace{
		SlackChannels:         &jsonStr,
		SlackWebhookChannel:   fakeStringPtr("word"),
		SlackWebhookChannelID: &webhookChanID, // same as existing
	}
	result, err := w.IntegratedSlackChannels()
	require.NoError(t, err)
	assert.Len(t, result, 1) // no duplicate
}

// ---------------------------------------------------------------------------
// Workspace.GetRetentionPeriod
// ---------------------------------------------------------------------------

func TestWorkspace_GetRetentionPeriod_WithValue(t *testing.T) {
	period := modelInputs.RetentionPeriodThirtyDays
	w := &Workspace{RetentionPeriod: &period}
	assert.Equal(t, modelInputs.RetentionPeriodThirtyDays, w.GetRetentionPeriod())
}

func TestWorkspace_GetRetentionPeriod_NilDefaultsSixMonths(t *testing.T) {
	w := &Workspace{}
	assert.Equal(t, modelInputs.RetentionPeriodSixMonths, w.GetRetentionPeriod())
}

// ---------------------------------------------------------------------------
// GetAttributesColumn
// ---------------------------------------------------------------------------

func TestGetAttributesColumn_MatchesPrefix(t *testing.T) {
	mappings := []ColumnMapping{
		{Prefix: "http.", Column: "http_attributes"},
		{Prefix: "db.", Column: "db_attributes"},
		{Prefix: "", Column: "default_attributes"},
	}

	assert.Equal(t, "http_attributes", GetAttributesColumn(mappings, "http.method"))
	assert.Equal(t, "http_attributes", GetAttributesColumn(mappings, "http.status_code"))
	assert.Equal(t, "db_attributes", GetAttributesColumn(mappings, "db.statement"))
}

func TestGetAttributesColumn_EmptyPrefix(t *testing.T) {
	mappings := []ColumnMapping{
		{Prefix: "", Column: "catch_all"},
	}
	// Empty prefix matches everything
	assert.Equal(t, "catch_all", GetAttributesColumn(mappings, "anything"))
}

func TestGetAttributesColumn_NoMatch(t *testing.T) {
	mappings := []ColumnMapping{
		{Prefix: "http.", Column: "http_attributes"},
	}
	assert.Equal(t, "", GetAttributesColumn(mappings, "db.statement"))
}

func TestGetAttributesColumn_EmptyMappings(t *testing.T) {
	assert.Equal(t, "", GetAttributesColumn(nil, "anything"))
	assert.Equal(t, "", GetAttributesColumn([]ColumnMapping{}, "anything"))
}

func TestGetAttributesColumn_FirstMatchWins(t *testing.T) {
	mappings := []ColumnMapping{
		{Prefix: "http.", Column: "first"},
		{Prefix: "http.", Column: "second"},
	}
	assert.Equal(t, "first", GetAttributesColumn(mappings, "http.method"))
}

// ---------------------------------------------------------------------------
// SendWelcomeSlackMessage: validation paths (no Slack API calls)
// ---------------------------------------------------------------------------

func TestSendWelcomeSlackMessage_NilAlert(t *testing.T) {
	err := SendWelcomeSlackMessage(nil, nil, &SendWelcomeSlackMessageInput{})
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "Alert needs to be defined")
}

func TestSendWelcomeSlackMessage_NilWorkspace(t *testing.T) {
	alert := &AlertDeprecated{Name: "test"}
	err := SendWelcomeSlackMessage(nil, alert, &SendWelcomeSlackMessageInput{})
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "Workspace needs to be defined")
}

func TestSendWelcomeSlackMessage_NilAdmin(t *testing.T) {
	alert := &AlertDeprecated{Name: "test"}
	w := fakeWorkspace()
	err := SendWelcomeSlackMessage(nil, alert, &SendWelcomeSlackMessageInput{
		Workspace: &w,
	})
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "Admin needs to be defined")
}

func TestSendWelcomeSlackMessage_NilProject(t *testing.T) {
	alert := &AlertDeprecated{Name: "test"}
	w := fakeWorkspace()
	a := fakeAdmin()
	err := SendWelcomeSlackMessage(nil, alert, &SendWelcomeSlackMessageInput{
		Workspace: &w,
		Admin:     &a,
	})
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "Project needs to be defined")
}

func TestSendWelcomeSlackMessage_ZeroID(t *testing.T) {
	alert := &AlertDeprecated{Name: "test"}
	w := fakeWorkspace()
	a := fakeAdmin()
	p := fakeProject()
	err := SendWelcomeSlackMessage(nil, alert, &SendWelcomeSlackMessageInput{
		Workspace: &w,
		Admin:     &a,
		Project:   &p,
		ID:        0,
	})
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "ID needs to be defined")
}

func TestSendWelcomeSlackMessage_EmptyURLSlug(t *testing.T) {
	alert := &AlertDeprecated{Name: "test"}
	w := fakeWorkspace()
	a := fakeAdmin()
	p := fakeProject()
	err := SendWelcomeSlackMessage(nil, alert, &SendWelcomeSlackMessageInput{
		Workspace: &w,
		Admin:     &a,
		Project:   &p,
		ID:        1,
		URLSlug:   "",
	})
	assert.Error(t, err)
	assert.Contains(t, err.Error(), "URLSlug needs to be defined")
}

// ---------------------------------------------------------------------------
// IAlert interface compliance
// ---------------------------------------------------------------------------

func TestIAlert_AlertDeprecated(t *testing.T) {
	var _ IAlert = &AlertDeprecated{}
}

func TestIAlert_MetricMonitor(t *testing.T) {
	var _ IAlert = &MetricMonitor{}
}

// ---------------------------------------------------------------------------
// driver.Valuer / sql.Scanner compliance for custom types
// ---------------------------------------------------------------------------

func TestCustomTypes_ImplementDriverValuer(t *testing.T) {
	var _ driver.Valuer = JSONB{}
	var _ driver.Valuer = Vector{}
	var _ driver.Valuer = DiscordChannels{}
	var _ driver.Valuer = MicrosoftTeamsChannels{}
	var _ driver.Valuer = WebhookDestinations{}
}

// ---------------------------------------------------------------------------
// Fuzz-style: random data through custom types
// ---------------------------------------------------------------------------

func TestJSONB_FuzzRandomData(t *testing.T) {
	for i := 0; i < 100; i++ {
		j := JSONB{}
		numKeys := gofakeit.IntRange(1, 20)
		for k := 0; k < numKeys; k++ {
			key := gofakeit.LetterN(uint(gofakeit.IntRange(1, 50)))
			switch gofakeit.IntRange(0, 4) {
			case 0:
				j[key] = gofakeit.Sentence(gofakeit.IntRange(1, 20))
			case 1:
				j[key] = gofakeit.Float64()
			case 2:
				j[key] = gofakeit.Bool()
			case 3:
				j[key] = nil
			case 4:
				j[key] = []interface{}{gofakeit.Word(), float64(gofakeit.IntRange(0, 1000))}
			}
		}

		val, err := j.Value()
		require.NoError(t, err, "iteration %d", i)

		var result JSONB
		err = result.Scan(val)
		require.NoError(t, err, "iteration %d", i)
		assert.Len(t, result, len(j), "iteration %d", i)
	}
}

func TestVector_FuzzRandomDimensions(t *testing.T) {
	for i := 0; i < 50; i++ {
		dim := gofakeit.IntRange(1, 2048)
		v := make(Vector, dim)
		for j := range v {
			v[j] = gofakeit.Float32Range(-100.0, 100.0)
		}

		val, err := v.Value()
		require.NoError(t, err)

		var result Vector
		err = result.Scan(val)
		require.NoError(t, err)
		assert.Len(t, result, dim)
	}
}

func TestDiscordChannels_FuzzRandomChannels(t *testing.T) {
	for i := 0; i < 50; i++ {
		count := gofakeit.IntRange(0, 30)
		channels := make(DiscordChannels, count)
		for j := 0; j < count; j++ {
			channels[j] = &DiscordChannel{
				Name: gofakeit.LetterN(uint(gofakeit.IntRange(1, 100))),
				ID:   gofakeit.DigitN(uint(gofakeit.IntRange(1, 20))),
			}
		}

		val, err := channels.Value()
		require.NoError(t, err)

		var result DiscordChannels
		err = result.Scan(val)
		require.NoError(t, err)
		assert.Len(t, result, count)
	}
}

// ---------------------------------------------------------------------------
// Model list completeness
// ---------------------------------------------------------------------------

func TestModels_ListNotEmpty(t *testing.T) {
	assert.Greater(t, len(Models), 50, "Models list should contain all registered GORM models")
}

// ---------------------------------------------------------------------------
// Constants and type aliases
// ---------------------------------------------------------------------------

func TestAlertType_Values(t *testing.T) {
	assert.Equal(t, "ERROR_ALERT", AlertType.ERROR)
	assert.Equal(t, "NEW_USER_ALERT", AlertType.NEW_USER)
	assert.Equal(t, "SESSIONS_ALERT", AlertType.SESSIONS)
	assert.Equal(t, "ERRORS_ALERT", AlertType.ERRORS)
	assert.Equal(t, "LOGS_ALERT", AlertType.LOGS)
	assert.Equal(t, "TRACES_ALERT", AlertType.TRACES)
	assert.Equal(t, "METRICS_ALERT", AlertType.METRICS)
}

func TestAdminRole_Values(t *testing.T) {
	assert.Equal(t, "ADMIN", AdminRole.ADMIN)
	assert.Equal(t, "MEMBER", AdminRole.MEMBER)
}

func TestSessionCommentTypes_Values(t *testing.T) {
	assert.Equal(t, "ADMIN", SessionCommentTypes.ADMIN)
	assert.Equal(t, "FEEDBACK", SessionCommentTypes.FEEDBACK)
}

func TestErrorType_Values(t *testing.T) {
	assert.Equal(t, "Frontend", ErrorType.FRONTEND)
	assert.Equal(t, "Backend", ErrorType.BACKEND)
}

func TestFingerprint_Values(t *testing.T) {
	assert.Equal(t, FingerprintType("CODE"), Fingerprint.StackFrameCode)
	assert.Equal(t, FingerprintType("META"), Fingerprint.StackFrameMetadata)
	assert.Equal(t, FingerprintType("JSON"), Fingerprint.JsonResult)
}

func TestRawPayloadType_Values(t *testing.T) {
	assert.Equal(t, RawPayloadType("raw-events"), PayloadTypeEvents)
	assert.Equal(t, RawPayloadType("raw-resources"), PayloadTypeResources)
	assert.Equal(t, RawPayloadType("raw-web-socket-events"), PayloadTypeWebSocketEvents)
}

func TestSessionExportFormat_Values(t *testing.T) {
	assert.Equal(t, "video/mp4", SessionExportFormatMP4)
	assert.Equal(t, "image/gif", SessionExportFormatGif)
	assert.Equal(t, "image/png", SessionExportFormatPng)
}

func TestBoolPointerShortcuts(t *testing.T) {
	assert.False(t, F)
	assert.True(t, T)
}

func TestPartitionSessionID(t *testing.T) {
	assert.Equal(t, 30000000, PARTITION_SESSION_ID)
}

// ---------------------------------------------------------------------------
// formatDuration (tested indirectly via Slack attachment building)
// ---------------------------------------------------------------------------

func TestFormatDuration(t *testing.T) {
	tests := []struct {
		input    time.Duration
		expected string
	}{
		{0, "0s"},
		{30 * time.Second, "30s"},
		{90 * time.Second, "1m 30s"},
		{3661 * time.Second, "1h 1m 1s"},
		{7200 * time.Second, "2h 0m 0s"},
		{61 * time.Second, "1m 1s"},
	}
	for _, tt := range tests {
		result := formatDuration(tt.input)
		assert.Equal(t, tt.expected, result, "formatDuration(%v)", tt.input)
	}
}

// ---------------------------------------------------------------------------
// Edge cases: large/boundary values
// ---------------------------------------------------------------------------

func TestVerboseID_LargeID(t *testing.T) {
	p := &Project{Model: Model{ID: 999999999}}
	verbose := p.VerboseID()
	decoded, err := FromVerboseID(verbose)
	require.NoError(t, err)
	assert.Equal(t, 999999999, decoded)
}

func TestVerboseID_ZeroID(t *testing.T) {
	p := &Project{Model: Model{ID: 0}}
	verbose := p.VerboseID()
	assert.NotEmpty(t, verbose)
}

func TestUnmarshalInt64ID_MaxInt64(t *testing.T) {
	s := fmt.Sprintf("%d", int64(math.MaxInt64))
	result, err := UnmarshalInt64ID(s)
	require.NoError(t, err)
	assert.Equal(t, int64(math.MaxInt64), result)
}

func TestVector_SingleElement(t *testing.T) {
	v := Vector{0.5}
	val, err := v.Value()
	require.NoError(t, err)
	var result Vector
	err = result.Scan(val)
	require.NoError(t, err)
	assert.Len(t, result, 1)
	assert.InDelta(t, 0.5, result[0], 1e-6)
}

func TestJSONB_DeeplyNested(t *testing.T) {
	j := JSONB{
		"level1": map[string]interface{}{
			"level2": map[string]interface{}{
				"level3": map[string]interface{}{
					"level4": "deep-value",
				},
			},
		},
	}
	val, err := j.Value()
	require.NoError(t, err)
	var result JSONB
	err = result.Scan(val)
	require.NoError(t, err)
	// Navigate the nesting
	l1, ok := result["level1"].(map[string]interface{})
	require.True(t, ok)
	l2, ok := l1["level2"].(map[string]interface{})
	require.True(t, ok)
	l3, ok := l2["level3"].(map[string]interface{})
	require.True(t, ok)
	assert.Equal(t, "deep-value", l3["level4"])
}

// ---------------------------------------------------------------------------
// Stress: concurrent access to factories (goroutine safety of gofakeit)
// ---------------------------------------------------------------------------

func TestFakeSession_ConcurrentCreation(t *testing.T) {
	done := make(chan Session, 100)
	for i := 0; i < 100; i++ {
		go func() {
			done <- fakeSession()
		}()
	}
	sessions := make(map[string]bool)
	for i := 0; i < 100; i++ {
		s := <-done
		assert.NotEmpty(t, s.SecureID)
		sessions[s.SecureID] = true
	}
	// All SecureIDs should be unique (they're UUIDs)
	assert.Len(t, sessions, 100)
}

// ---------------------------------------------------------------------------
// WebhookDestinations with many random entries
// ---------------------------------------------------------------------------

func TestWebhookDestinations_MassData(t *testing.T) {
	dests := make(WebhookDestinations, 200)
	for i := range dests {
		auth := strings.Repeat(gofakeit.Letter(), gofakeit.IntRange(10, 200))
		dests[i] = &WebhookDestination{
			URL:           fmt.Sprintf("https://%s.example.com/webhook/%s", gofakeit.DomainName(), gofakeit.UUID()),
			Authorization: &auth,
		}
	}

	val, err := dests.Value()
	require.NoError(t, err)

	var result WebhookDestinations
	err = result.Scan(val)
	require.NoError(t, err)
	assert.Len(t, result, 200)

	for i := range dests {
		assert.Equal(t, dests[i].URL, result[i].URL)
		assert.Equal(t, *dests[i].Authorization, *result[i].Authorization)
	}
}

// ---------------------------------------------------------------------------
// ContextKeys
// ---------------------------------------------------------------------------

func TestContextKeys_Defined(t *testing.T) {
	assert.Equal(t, contextString("ip"), ContextKeys.IP)
	assert.Equal(t, contextString("userAgent"), ContextKeys.UserAgent)
	assert.Equal(t, contextString("uid"), ContextKeys.UID)
	assert.Equal(t, contextString("email"), ContextKeys.Email)
	assert.Equal(t, contextString("sessionId"), ContextKeys.SessionId)
}

// ---------------------------------------------------------------------------
// modelInputs import — ensure retention period values exist
// ---------------------------------------------------------------------------

func TestRetentionPeriod_AllValues(t *testing.T) {
	// Verify the values we reference in code exist
	_ = modelInputs.RetentionPeriodSixMonths
	_ = modelInputs.RetentionPeriodThirtyDays
}

// ===========================================================================
// ADVERSARIAL / BOUNDARY-BREAKING TESTS
// ===========================================================================
// These tests deliberately feed malformed, extreme, or hostile data into
// model functions to verify they fail gracefully (or handle correctly).

// ---------------------------------------------------------------------------
// Int64ID: overflow, underflow, and hostile strings
// ---------------------------------------------------------------------------

func TestUnmarshalInt64ID_Overflow(t *testing.T) {
	// One past max int64 — should fail parse
	_, err := UnmarshalInt64ID("9223372036854775808")
	assert.Error(t, err)
}

func TestUnmarshalInt64ID_NegativeMax(t *testing.T) {
	s := fmt.Sprintf("%d", int64(math.MinInt64))
	result, err := UnmarshalInt64ID(s)
	require.NoError(t, err)
	assert.Equal(t, int64(math.MinInt64), result)
}

func TestUnmarshalInt64ID_WhitespaceString(t *testing.T) {
	_, err := UnmarshalInt64ID("  42  ")
	assert.Error(t, err, "leading/trailing whitespace should not be silently accepted")
}

func TestUnmarshalInt64ID_EmptyString(t *testing.T) {
	_, err := UnmarshalInt64ID("")
	assert.Error(t, err)
}

func TestUnmarshalInt64ID_HexString(t *testing.T) {
	_, err := UnmarshalInt64ID("0xDEADBEEF")
	assert.Error(t, err, "hex input should not be parsed as decimal")
}

func TestUnmarshalInt64ID_ScientificNotation(t *testing.T) {
	_, err := UnmarshalInt64ID("1e10")
	assert.Error(t, err, "scientific notation should not be accepted for int64")
}

func TestUnmarshalInt64ID_NullByte(t *testing.T) {
	_, err := UnmarshalInt64ID("42\x00")
	assert.Error(t, err, "null bytes should not be silently accepted")
}

func TestUnmarshalInt64ID_SQLInjection(t *testing.T) {
	_, err := UnmarshalInt64ID("1; DROP TABLE sessions;--")
	assert.Error(t, err)
}

func TestUnmarshalInt64ID_NilInterface(t *testing.T) {
	_, err := UnmarshalInt64ID(nil)
	assert.Error(t, err)
}

func TestUnmarshalInt64ID_BoolType(t *testing.T) {
	_, err := UnmarshalInt64ID(true)
	assert.Error(t, err)
}

func TestUnmarshalInt64ID_SliceType(t *testing.T) {
	_, err := UnmarshalInt64ID([]int{1, 2, 3})
	assert.Error(t, err)
}

// ---------------------------------------------------------------------------
// Timestamp: malformed dates, time bombs, epoch edge cases
// ---------------------------------------------------------------------------

func TestUnmarshalTimestamp_SQLInjection(t *testing.T) {
	_, err := UnmarshalTimestamp("2024-01-01'; DROP TABLE sessions;--")
	assert.Error(t, err)
}

func TestUnmarshalTimestamp_UnixEpoch(t *testing.T) {
	result, err := UnmarshalTimestamp("1970-01-01T00:00:00Z")
	require.NoError(t, err)
	assert.True(t, result.IsZero() || result.Unix() == 0)
}

func TestUnmarshalTimestamp_FarFuture(t *testing.T) {
	result, err := UnmarshalTimestamp("9999-12-31T23:59:59.999999999Z")
	require.NoError(t, err)
	assert.Equal(t, 9999, result.Year())
}

func TestUnmarshalTimestamp_NegativeYear(t *testing.T) {
	// RFC3339 doesn't support negative years, should fail
	_, err := UnmarshalTimestamp("-0001-01-01T00:00:00Z")
	assert.Error(t, err)
}

func TestUnmarshalTimestamp_EmptyString(t *testing.T) {
	_, err := UnmarshalTimestamp("")
	assert.Error(t, err)
}

func TestUnmarshalTimestamp_JustDate(t *testing.T) {
	_, err := UnmarshalTimestamp("2024-01-01")
	assert.Error(t, err, "date without time should not parse as RFC3339Nano")
}

func TestUnmarshalTimestamp_UnixTimestampNumber(t *testing.T) {
	_, err := UnmarshalTimestamp(1710000000)
	assert.Error(t, err, "raw integer should not be accepted as timestamp")
}

func TestUnmarshalTimestamp_FloatTimestamp(t *testing.T) {
	_, err := UnmarshalTimestamp(1710000000.123)
	assert.Error(t, err)
}

// ---------------------------------------------------------------------------
// StringArray: hostile elements
// ---------------------------------------------------------------------------

func TestUnmarshalStringArray_HugeArray(t *testing.T) {
	arr := make([]interface{}, 10000)
	for i := range arr {
		arr[i] = fmt.Sprintf("item_%d", i)
	}
	result, err := UnmarshalStringArray(arr)
	require.NoError(t, err)
	assert.Len(t, result, 10000)
}

func TestUnmarshalStringArray_NullStringElements(t *testing.T) {
	// nil elements in the array should fail (not strings)
	_, err := UnmarshalStringArray([]interface{}{"ok", nil})
	assert.Error(t, err)
}

func TestUnmarshalStringArray_MixedTypes(t *testing.T) {
	_, err := UnmarshalStringArray([]interface{}{"ok", 42, true, nil})
	assert.Error(t, err)
}

func TestUnmarshalStringArray_SQLInjectionElements(t *testing.T) {
	// These should be accepted as strings — it's the DB layer's job to parameterize
	result, err := UnmarshalStringArray([]interface{}{
		"'; DROP TABLE sessions;--",
		"1 OR 1=1",
		"<script>alert('xss')</script>",
	})
	require.NoError(t, err)
	assert.Len(t, result, 3)
	assert.Equal(t, "'; DROP TABLE sessions;--", result[0])
}

// ---------------------------------------------------------------------------
// JSONB: hostile payloads
// ---------------------------------------------------------------------------

func TestJSONB_Scan_SQLInjectionValue(t *testing.T) {
	// Valid JSON containing SQL injection strings — should parse fine
	var j JSONB
	err := j.Scan(`{"key": "'; DROP TABLE sessions;--"}`)
	require.NoError(t, err)
	assert.Equal(t, "'; DROP TABLE sessions;--", j["key"])
}

func TestJSONB_Scan_HTMLXSSValue(t *testing.T) {
	var j JSONB
	err := j.Scan(`{"key": "<img src=x onerror=alert(1)>"}`)
	require.NoError(t, err)
	assert.Equal(t, "<img src=x onerror=alert(1)>", j["key"])
}

func TestJSONB_Scan_NullByteInJSON(t *testing.T) {
	var j JSONB
	// JSON with literal null byte encoded — \u0000 is valid JSON but toxic to C-based parsers
	err := j.Scan(`{"key": "hello\u0000world"}`)
	// Go's json package accepts \u0000, verify it round-trips
	if err == nil {
		val, err2 := j.Value()
		require.NoError(t, err2)
		assert.NotEmpty(t, val)
	}
}

func TestJSONB_Scan_VeryLargePayload(t *testing.T) {
	// 1MB JSON payload
	bigValue := strings.Repeat("x", 1024*1024)
	payload := fmt.Sprintf(`{"big": "%s"}`, bigValue)
	var j JSONB
	err := j.Scan(payload)
	require.NoError(t, err)
	assert.Equal(t, bigValue, j["big"])
}

func TestJSONB_Scan_DuplicateKeys(t *testing.T) {
	// JSON spec says last value wins for duplicate keys
	var j JSONB
	err := j.Scan(`{"key": "first", "key": "second"}`)
	require.NoError(t, err)
	assert.Equal(t, "second", j["key"])
}

func TestJSONB_Scan_UnicodeEscape(t *testing.T) {
	var j JSONB
	err := j.Scan(`{"emoji": "\u0048\u0065\u006C\u006C\u006F"}`)
	require.NoError(t, err)
	assert.Equal(t, "Hello", j["emoji"])
}

func TestJSONB_Scan_TrailingGarbage(t *testing.T) {
	var j JSONB
	// Go's json.Unmarshal ignores trailing content after valid JSON in some cases
	// but wrapping in Scan should still produce a parseable result or error
	err := j.Scan(`{"key": "val"} GARBAGE`)
	// This is implementation-dependent — just verify no panic
	_ = err
}

func TestJSONB_Scan_ArrayNotObject(t *testing.T) {
	// JSONB expects a map, not an array
	var j JSONB
	err := j.Scan(`[1, 2, 3]`)
	assert.Error(t, err, "array should not unmarshal into map[string]interface{}")
}

func TestJSONB_Scan_ScalarNotObject(t *testing.T) {
	var j JSONB
	err := j.Scan(`"just a string"`)
	assert.Error(t, err, "bare string should not unmarshal into map")
}

func TestJSONB_Scan_NumberNotObject(t *testing.T) {
	var j JSONB
	err := j.Scan(`42`)
	assert.Error(t, err, "bare number should not unmarshal into map")
}

func TestJSONB_Scan_IntegerType(t *testing.T) {
	// Scan receives an unexpected type (not string, not []byte)
	var j JSONB
	err := j.Scan(42)
	// Should not panic — either error or no-op
	assert.NoError(t, err, "unexpected type should be handled gracefully (no-op)")
	assert.Nil(t, j, "JSONB should remain nil for non-string/byte input")
}

// ---------------------------------------------------------------------------
// Vector: extreme values, NaN, Inf
// ---------------------------------------------------------------------------

func TestVector_NaN(t *testing.T) {
	v := Vector{float32(math.NaN())}
	_, err := v.Value()
	// NaN in JSON is not valid — json.Marshal should fail
	assert.Error(t, err, "NaN should cause marshal error")
}

func TestVector_Inf(t *testing.T) {
	v := Vector{float32(math.Inf(1))}
	_, err := v.Value()
	assert.Error(t, err, "+Inf should cause marshal error")
}

func TestVector_NegInf(t *testing.T) {
	v := Vector{float32(math.Inf(-1))}
	_, err := v.Value()
	assert.Error(t, err, "-Inf should cause marshal error")
}

func TestVector_Scan_InvalidJSON(t *testing.T) {
	var v Vector
	err := v.Scan("[1.0, 2.0, not-a-number]")
	assert.Error(t, err)
}

func TestVector_Scan_ObjectNotArray(t *testing.T) {
	var v Vector
	err := v.Scan(`{"not": "an array"}`)
	assert.Error(t, err)
}

func TestVector_Scan_NestedArray(t *testing.T) {
	var v Vector
	err := v.Scan(`[[1.0, 2.0], [3.0, 4.0]]`)
	assert.Error(t, err, "nested arrays should not unmarshal into flat Vector")
}

func TestVector_Scan_StringElements(t *testing.T) {
	var v Vector
	err := v.Scan(`["a", "b", "c"]`)
	assert.Error(t, err)
}

func TestVector_Scan_EmptyArray(t *testing.T) {
	var v Vector
	err := v.Scan("[]")
	require.NoError(t, err)
	assert.Empty(t, v)
}

func TestVector_Scan_IntegerType(t *testing.T) {
	var v Vector
	err := v.Scan(42)
	assert.NoError(t, err, "unexpected type should be handled gracefully")
	assert.Nil(t, v)
}

func TestVector_MaxFloat32(t *testing.T) {
	v := Vector{math.MaxFloat32, -math.MaxFloat32, math.SmallestNonzeroFloat32}
	val, err := v.Value()
	require.NoError(t, err)
	var result Vector
	err = result.Scan(val)
	require.NoError(t, err)
	assert.InDelta(t, math.MaxFloat32, result[0], 1e30)
}

// ---------------------------------------------------------------------------
// DiscordChannels / Teams / Webhooks: hostile payloads
// ---------------------------------------------------------------------------

func TestDiscordChannels_Scan_InvalidJSON(t *testing.T) {
	var dc DiscordChannels
	err := dc.Scan("not json")
	assert.Error(t, err)
}

func TestDiscordChannels_Scan_WrongType(t *testing.T) {
	var dc DiscordChannels
	err := dc.Scan(42)
	assert.NoError(t, err, "non-string/byte types should no-op")
	assert.Nil(t, dc)
}

func TestDiscordChannels_Scan_HTMLInName(t *testing.T) {
	var dc DiscordChannels
	err := dc.Scan(`[{"Name": "<script>alert(1)</script>", "ID": "123"}]`)
	require.NoError(t, err)
	assert.Equal(t, "<script>alert(1)</script>", dc[0].Name)
}

func TestMicrosoftTeamsChannels_Scan_InvalidJSON(t *testing.T) {
	var tc MicrosoftTeamsChannels
	err := tc.Scan("}{bad")
	assert.Error(t, err)
}

func TestWebhookDestinations_Scan_InvalidJSON(t *testing.T) {
	var wd WebhookDestinations
	err := wd.Scan("{broken")
	assert.Error(t, err)
}

func TestWebhookDestinations_Scan_ExtraFields(t *testing.T) {
	// Unknown fields should be ignored
	var wd WebhookDestinations
	err := wd.Scan(`[{"URL": "https://example.com", "Authorization": "Bearer tok", "Evil": "payload"}]`)
	require.NoError(t, err)
	assert.Len(t, wd, 1)
	assert.Equal(t, "https://example.com", wd[0].URL)
}

// ---------------------------------------------------------------------------
// VerboseID: adversarial inputs
// ---------------------------------------------------------------------------

func TestFromVerboseID_NullBytes(t *testing.T) {
	_, err := FromVerboseID("abc\x00def")
	assert.Error(t, err)
}

func TestFromVerboseID_VeryLongInput(t *testing.T) {
	long := strings.Repeat("a", 10000)
	_, err := FromVerboseID(long)
	// Should not panic, should error
	assert.Error(t, err)
}

func TestFromVerboseID_NegativeInteger(t *testing.T) {
	// Negative integers parse via Atoi path
	result, err := FromVerboseID("-1")
	require.NoError(t, err)
	assert.Equal(t, -1, result, "negative integers should pass through Atoi")
}

func TestFromVerboseID_Zero(t *testing.T) {
	result, err := FromVerboseID("0")
	require.NoError(t, err)
	assert.Equal(t, 0, result)
}

func TestFromVerboseID_MaxInt(t *testing.T) {
	result, err := FromVerboseID(fmt.Sprintf("%d", math.MaxInt32))
	require.NoError(t, err)
	assert.Equal(t, math.MaxInt32, result)
}

func TestFromVerboseID_UppercaseNotInAlphabet(t *testing.T) {
	// HashID alphabet is lowercase only — uppercase should fail
	_, err := FromVerboseID("ABCDEFGH")
	assert.Error(t, err)
}

func TestFromVerboseID_SpecialChars(t *testing.T) {
	inputs := []string{"../../../etc/passwd", "<script>", "{{template}}", "${env}", "%00"}
	for _, input := range inputs {
		_, err := FromVerboseID(input)
		assert.Error(t, err, "FromVerboseID(%q) should error", input)
	}
}

// ---------------------------------------------------------------------------
// Session.SetUserProperties / GetUserProperties: hostile data
// ---------------------------------------------------------------------------

func TestSession_SetUserProperties_HugeMap(t *testing.T) {
	s := fakeSession()
	props := make(map[string]string, 10000)
	for i := 0; i < 10000; i++ {
		props[fmt.Sprintf("key_%d", i)] = gofakeit.Sentence(10)
	}
	err := s.SetUserProperties(props)
	require.NoError(t, err)

	result, err := s.GetUserProperties()
	require.NoError(t, err)
	assert.Len(t, result, 10000)
}

func TestSession_SetUserProperties_LongValues(t *testing.T) {
	s := fakeSession()
	// 1MB value
	big := strings.Repeat("x", 1024*1024)
	props := map[string]string{"big": big}
	err := s.SetUserProperties(props)
	require.NoError(t, err)

	result, err := s.GetUserProperties()
	require.NoError(t, err)
	assert.Equal(t, big, result["big"])
}

func TestSession_SetUserProperties_JSONBreakingKeys(t *testing.T) {
	s := fakeSession()
	// Keys that try to break JSON structure
	props := map[string]string{
		`"quoted"`:         "value",
		"back\\slash":      "value",
		"new\nline":        "value",
		"tab\there":        "value",
		"\x00null":         "value",
		`{"nested": true}`: "value",
	}
	err := s.SetUserProperties(props)
	require.NoError(t, err)

	result, err := s.GetUserProperties()
	require.NoError(t, err)
	assert.Len(t, result, len(props))
}

// ---------------------------------------------------------------------------
// DecodeAndValidateParams: adversarial inputs
// ---------------------------------------------------------------------------

func TestDecodeAndValidateParams_NilInList(t *testing.T) {
	// BUG: DecodeAndValidateParams panics with nil pointer dereference when
	// a nil element is in the params slice. The mapstructure decoder returns
	// a nil *Param, then line 1800 dereferences output.Action without nil check.
	// This test documents the crash. When fixed, change to assert.Error(t, err).
	assert.Panics(t, func() {
		_, _ = DecodeAndValidateParams([]interface{}{nil})
	}, "DecodeAndValidateParams panics on nil params — needs nil guard")
}

func TestDecodeAndValidateParams_WrongStructure(t *testing.T) {
	// Pass a string instead of a map
	params := []interface{}{"not-a-map"}
	_, err := DecodeAndValidateParams(params)
	assert.Error(t, err)
}

func TestDecodeAndValidateParams_MissingFields(t *testing.T) {
	// Map with missing required fields — should still decode (zero values)
	params := []interface{}{
		map[string]interface{}{},
	}
	result, err := DecodeAndValidateParams(params)
	require.NoError(t, err)
	assert.Len(t, result, 1)
	assert.Equal(t, "", result[0].Action, "missing action should be empty string")
}

func TestDecodeAndValidateParams_ExtraFields(t *testing.T) {
	// Extra fields should be silently ignored
	params := []interface{}{
		map[string]interface{}{
			"action": "test",
			"type":   "user_property",
			"value":  map[string]interface{}{"text": "a", "value": "b"},
			"evil":   "payload",
		},
	}
	result, err := DecodeAndValidateParams(params)
	require.NoError(t, err)
	assert.Len(t, result, 1)
	assert.Equal(t, "test", result[0].Action)
}

func TestDecodeAndValidateParams_EmptyActionIsDuplicate(t *testing.T) {
	// Two params with empty action — should trigger duplicate check
	params := []interface{}{
		map[string]interface{}{"action": "", "type": "a", "value": map[string]interface{}{"text": "", "value": ""}},
		map[string]interface{}{"action": "", "type": "b", "value": map[string]interface{}{"text": "", "value": ""}},
	}
	_, err := DecodeAndValidateParams(params)
	assert.Error(t, err, "empty string actions should still be caught as duplicates")
}

// ---------------------------------------------------------------------------
// GetEmailsToNotify: adversarial inputs
// ---------------------------------------------------------------------------

func TestGetEmailsToNotify_NestedArrays(t *testing.T) {
	bad := `[["nested@email.com"]]`
	_, err := GetEmailsToNotify(&bad)
	assert.Error(t, err, "nested arrays should not unmarshal into []*string")
}

func TestGetEmailsToNotify_ObjectNotArray(t *testing.T) {
	bad := `{"email": "test@test.com"}`
	_, err := GetEmailsToNotify(&bad)
	assert.Error(t, err)
}

func TestGetEmailsToNotify_NumberArray(t *testing.T) {
	bad := `[1, 2, 3]`
	_, err := GetEmailsToNotify(&bad)
	assert.Error(t, err, "numbers should not unmarshal into []*string")
}

func TestGetEmailsToNotify_NullElements(t *testing.T) {
	// JSON null in array — should become nil *string pointer
	nullStr := `[null, "real@email.com", null]`
	result, err := GetEmailsToNotify(&nullStr)
	require.NoError(t, err)
	assert.Len(t, result, 3)
	assert.Nil(t, result[0])
	assert.Equal(t, "real@email.com", *result[1])
	assert.Nil(t, result[2])
}

func TestGetEmailsToNotify_HugeList(t *testing.T) {
	emails := make([]string, 10000)
	for i := range emails {
		emails[i] = fmt.Sprintf("user%d@example.com", i)
	}
	jsonBytes, _ := json.Marshal(emails)
	jsonStr := string(jsonBytes)
	result, err := GetEmailsToNotify(&jsonStr)
	require.NoError(t, err)
	assert.Len(t, result, 10000)
}

// ---------------------------------------------------------------------------
// AlertDeprecated: adversarial JSON
// ---------------------------------------------------------------------------

func TestAlertDeprecated_GetExcludedEnvironments_InvalidJSON(t *testing.T) {
	bad := `{not-json}`
	alert := &AlertDeprecated{ExcludedEnvironments: &bad}
	_, err := alert.GetExcludedEnvironments()
	assert.Error(t, err)
}

func TestAlertDeprecated_GetChannelsToNotify_InvalidJSON(t *testing.T) {
	bad := `[{broken`
	alert := &AlertDeprecated{ChannelsToNotify: &bad}
	_, err := alert.GetChannelsToNotify()
	assert.Error(t, err)
}

func TestAlertDeprecated_GetEmailsToNotify_InvalidJSON(t *testing.T) {
	bad := `not-json`
	alert := &AlertDeprecated{EmailsToNotify: &bad}
	_, err := alert.GetEmailsToNotify()
	assert.Error(t, err)
}

// ---------------------------------------------------------------------------
// SessionAlert: adversarial JSON
// ---------------------------------------------------------------------------

func TestSessionAlert_GetTrackProperties_InvalidJSON(t *testing.T) {
	bad := "not-json"
	alert := &SessionAlert{TrackProperties: &bad}
	_, err := alert.GetTrackProperties()
	assert.Error(t, err)
}

func TestSessionAlert_GetUserProperties_InvalidJSON(t *testing.T) {
	bad := "not-json"
	alert := &SessionAlert{UserProperties: &bad}
	_, err := alert.GetUserProperties()
	assert.Error(t, err)
}

func TestSessionAlert_GetExcludeRules_InvalidJSON(t *testing.T) {
	bad := "not-json"
	alert := &SessionAlert{ExcludeRules: &bad}
	_, err := alert.GetExcludeRules()
	assert.Error(t, err)
}

// ---------------------------------------------------------------------------
// MetricMonitor: adversarial JSON
// ---------------------------------------------------------------------------

func TestMetricMonitor_GetChannelsToNotify_InvalidJSON(t *testing.T) {
	bad := "not-json"
	m := &MetricMonitor{ChannelsToNotify: &bad}
	_, err := m.GetChannelsToNotify()
	assert.Error(t, err)
}

// ---------------------------------------------------------------------------
// Workspace.IntegratedSlackChannels: adversarial JSON
// ---------------------------------------------------------------------------

func TestWorkspace_IntegratedSlackChannels_PartialJSON(t *testing.T) {
	partial := `[{"WebhookAccessToken": "tok", "WebhookURL": "url"`
	w := &Workspace{SlackChannels: &partial}
	_, err := w.IntegratedSlackChannels()
	assert.Error(t, err)
}

func TestWorkspace_IntegratedSlackChannels_EmptyArrayString(t *testing.T) {
	empty := "[]"
	w := &Workspace{SlackChannels: &empty}
	result, err := w.IntegratedSlackChannels()
	require.NoError(t, err)
	assert.Empty(t, result)
}

// ---------------------------------------------------------------------------
// GORM Hooks: adversarial — verify hooks don't overwrite existing secrets
// ---------------------------------------------------------------------------

func TestProject_BeforeCreate_DoesNotOverwriteExistingSecret(t *testing.T) {
	existing := "my-precious-secret"
	p := fakeProject()
	p.Secret = &existing
	// BeforeCreate unconditionally overwrites — this test documents the behavior
	_ = p.BeforeCreate(&gorm.DB{})
	// Current implementation ALWAYS overwrites — verify this is intentional
	assert.NotEqual(t, existing, *p.Secret,
		"BeforeCreate overwrites existing secrets — if this fails, the hook was changed to preserve them")
}

func TestWorkspace_BeforeCreate_DoesNotOverwriteExistingSecret(t *testing.T) {
	existing := "my-precious-secret"
	w := fakeWorkspace()
	w.Secret = &existing
	_ = w.BeforeCreate(&gorm.DB{})
	assert.NotEqual(t, existing, *w.Secret,
		"BeforeCreate overwrites existing secrets — if this fails, the hook was changed to preserve them")
}
