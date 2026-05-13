{{/*
Common helpers for the HoldFast chart.
*/}}

{{/*
Expand the name of the chart.
*/}}
{{- define "holdfast.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a fully-qualified app name. Honors fullnameOverride or builds
"<release>-<chart>" by default. Truncated to 63 chars for k8s name limits.
*/}}
{{- define "holdfast.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Chart name + version for the helm.sh/chart label.
*/}}
{{- define "holdfast.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels shared by every object in the release.
*/}}
{{- define "holdfast.labels" -}}
helm.sh/chart: {{ include "holdfast.chart" . }}
{{ include "holdfast.selectorLabels" . }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: holdfast
{{- end }}

{{/*
Selector labels — release-stable, used in selector and matchLabels.
*/}}
{{- define "holdfast.selectorLabels" -}}
app.kubernetes.io/name: {{ include "holdfast.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Component-scoped labels for the backend pod.
*/}}
{{- define "holdfast.backend.labels" -}}
{{ include "holdfast.labels" . }}
app.kubernetes.io/component: backend
{{- end }}

{{- define "holdfast.backend.selectorLabels" -}}
{{ include "holdfast.selectorLabels" . }}
app.kubernetes.io/component: backend
{{- end }}

{{/*
Component-scoped labels for the postgres pod.
*/}}
{{- define "holdfast.postgres.labels" -}}
{{ include "holdfast.labels" . }}
app.kubernetes.io/component: postgres
{{- end }}

{{- define "holdfast.postgres.selectorLabels" -}}
{{ include "holdfast.selectorLabels" . }}
app.kubernetes.io/component: postgres
{{- end }}

{{/*
ServiceAccount name — honors create=false by letting users specify a
pre-existing SA name.
*/}}
{{- define "holdfast.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "holdfast.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
Backend image reference. Defaults the tag to .Chart.AppVersion when empty.
*/}}
{{- define "holdfast.image" -}}
{{- $registry := .Values.image.registry -}}
{{- $repo := .Values.image.repository -}}
{{- $tag := default .Chart.AppVersion .Values.image.tag -}}
{{- if $registry -}}
{{- printf "%s/%s:%s" $registry $repo $tag -}}
{{- else -}}
{{- printf "%s:%s" $repo $tag -}}
{{- end -}}
{{- end }}

{{/*
Postgres host — chart-managed StatefulSet service name OR external host.
*/}}
{{- define "holdfast.postgres.host" -}}
{{- if .Values.postgres.enabled -}}
{{- printf "%s-postgres" (include "holdfast.fullname" .) -}}
{{- else -}}
{{- .Values.externalPostgres.host -}}
{{- end -}}
{{- end }}

{{- define "holdfast.postgres.port" -}}
{{- if .Values.postgres.enabled -}}
{{- .Values.postgres.service.port -}}
{{- else -}}
{{- .Values.externalPostgres.port -}}
{{- end -}}
{{- end }}

{{- define "holdfast.postgres.user" -}}
{{- if .Values.postgres.enabled -}}
{{- .Values.postgres.auth.user -}}
{{- else -}}
{{- .Values.externalPostgres.user -}}
{{- end -}}
{{- end }}

{{/*
Name of the Secret that holds PSQL_PASSWORD.
*/}}
{{- define "holdfast.postgres.secretName" -}}
{{- if .Values.postgres.enabled -}}
{{- if .Values.postgres.auth.existingSecret -}}
{{- .Values.postgres.auth.existingSecret -}}
{{- else -}}
{{- printf "%s-postgres" (include "holdfast.fullname" .) -}}
{{- end -}}
{{- else -}}
{{- .Values.externalPostgres.passwordSecret.name -}}
{{- end -}}
{{- end }}

{{- define "holdfast.postgres.secretKey" -}}
{{- if .Values.postgres.enabled -}}
{{- .Values.postgres.auth.passwordKey -}}
{{- else -}}
{{- .Values.externalPostgres.passwordSecret.key -}}
{{- end -}}
{{- end }}
