FROM --platform=$BUILDPLATFORM golang:alpine AS backend-build

RUN apk update && apk add --no-cache build-base

WORKDIR /highlight
COPY go.work .
COPY go.work.sum .

COPY src/backend/go.mod ./src/backend/go.mod
COPY src/backend/go.sum ./src/backend/go.sum
COPY infra/docker/enterprise-public.pem ./enterprise-public.pem
COPY sdk/highlight-go/go.mod ./sdk/highlight-go/go.mod
COPY sdk/highlight-go/go.sum ./sdk/highlight-go/go.sum
COPY sdk/highlightinc-highlight-datasource/go.mod ./sdk/highlightinc-highlight-datasource/go.mod
COPY sdk/highlightinc-highlight-datasource/go.sum ./sdk/highlightinc-highlight-datasource/go.sum
COPY tests/e2e/go/go.mod ./tests/e2e/go/go.mod
COPY tests/e2e/go/go.sum ./tests/e2e/go/go.sum

RUN go work sync
RUN go mod download

COPY src/backend ./src/backend
COPY sdk/highlight-go ./sdk/highlight-go
COPY sdk/highlightinc-highlight-datasource ./sdk/highlightinc-highlight-datasource
COPY tests/e2e/go ./tests/e2e/go

WORKDIR /highlight/src/backend
ARG TARGETARCH
ARG TARGETOS
RUN export PUBKEY=`cat /highlight/enterprise-public.pem | base64 -w0` GOOS=$TARGETOS GOARCH=$TARGETARCH && \
    go build -ldflags="-X github.com/highlight-run/highlight/backend/env.EnterpriseEnvPublicKey=$PUBKEY" -o /build/backend

# reduce the image size by keeping just the built code
FROM alpine:latest AS backend-prod
ARG REACT_APP_COMMIT_SHA
ENV REACT_APP_COMMIT_SHA=$REACT_APP_COMMIT_SHA
ARG RELEASE
ENV RELEASE=$RELEASE
LABEL org.opencontainers.image.source=https://github.com/highlight/highlight
LABEL org.opencontainers.image.description="HoldFast Production Backend Image"
LABEL org.opencontainers.image.licenses="Apache 2.0"

RUN wget -q -t3 'https://packages.doppler.com/public/cli/rsa.8004D9FF50437357.key' -O /etc/apk/keys/cli@doppler-8004D9FF50437357.rsa.pub && \
    echo 'https://packages.doppler.com/public/cli/alpine/any-version/main' | tee -a /etc/apk/repositories && \
    apk add --no-cache curl doppler

WORKDIR /build
COPY --from=backend-build /build/backend /build
COPY --from=backend-build /highlight/src/backend/env.enc /build
COPY --from=backend-build /highlight/src/backend/env.enc.dgst /build
COPY --from=backend-build /highlight/src/backend/localhostssl/ /build/localhostssl
COPY --from=backend-build /highlight/src/backend/clickhouse/migrations/ /build/clickhouse/migrations

CMD ["/build/backend", "-runtime=all"]
