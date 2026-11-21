#!/bin/sh
# Creates demo data through the public API (so every rule is applied). Safe to run twice.
set -eu
API="${API_URL:-http://localhost:8080}"
EMAIL="${DEMO_EMAIL:-demo@invoicehub.example}"
PASS="${DEMO_PASSWORD:-Demo#Pass2026}"
json='Content-Type: application/json'

code=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$API/api/auth/register" -H "$json" -d "$(jq -n \
  --arg e "$EMAIL" --arg p "$PASS" '{businessName:"Demo Traders Pvt Ltd",gstin:"27AAPFU0939F1ZV",stateCode:"27",email:$e,password:$p}')")
if [ "$code" != "200" ] && [ "$code" != "201" ]; then
  echo "Register returned $code: demo tenant probably exists already, skipping seed."; exit 0
fi
TOKEN=$(curl -s -X POST "$API/api/auth/login" -H "$json" -d "$(jq -n --arg e "$EMAIL" --arg p "$PASS" '{email:$e,password:$p}')" | jq -r .accessToken)
auth="Authorization: Bearer $TOKEN"
# Records a payment the way the payment provider does: HMAC-SHA256 over "timestamp.body" with the shared secret.
pay() {
  body=$(jq -nc --arg e "seed-$1-$RANDOM" --arg i "$1" --argjson a "$2" '{eventId:$e,invoiceId:$i,amount:$a,currency:"INR",reference:"demo"}')
  ts=$(date +%s)
  sig=$(printf '%s.%s' "$ts" "$body" | openssl dgst -sha256 -hmac "$WEBHOOK_SECRET" | sed 's/^.* //')
  curl -sf -o /dev/null -X POST "$API/api/webhooks/payments" -H "$json" -H "X-Payment-Timestamp: $ts" -H "X-Payment-Signature: sha256=$sig" -d "$body"
}
post() { curl -sf -X POST "$API$1" -H "$json" -H "$auth" -d "$2"; }

# state 27 = same state as the seller (CGST+SGST); 29, 07, 24 = other states (IGST)
c1=$(post /api/customers '{"name":"Shah Hardware","email":"accounts@shah.example","gstin":"27BBBPS1234C1Z5","stateCode":"27","addressLine1":"12 MG Road","city":"Pune","postalCode":"411001"}' | jq -r .id)
c2=$(post /api/customers '{"name":"Kaveri Electronics","email":"buy@kaveri.example","gstin":"29AAACK5678D1Z2","stateCode":"29","addressLine1":"88 Brigade Road","city":"Bengaluru","postalCode":"560001"}' | jq -r .id)
c3=$(post /api/customers '{"name":"Delhi Office Supplies","email":"po@dos.example","gstin":"07AAACD4321E1Z9","stateCode":"07","addressLine1":"5 Connaught Place","city":"New Delhi","postalCode":"110001"}' | jq -r .id)
c4=$(post /api/customers '{"name":"Gujarat Stationers","email":"hello@gujstat.example","stateCode":"24","addressLine1":"3 Ashram Road","city":"Ahmedabad","postalCode":"380009"}' | jq -r .id)
p1=$(post /api/products '{"name":"Laptop stand","hsnCode":"8473","unitPrice":1499,"gstRate":18}' | jq -r .id)
p2=$(post /api/products '{"name":"Wireless mouse","hsnCode":"8471","unitPrice":799,"gstRate":18}' | jq -r .id)
p3=$(post /api/products '{"name":"A4 paper (500 sheets)","hsnCode":"4802","unitPrice":320,"gstRate":12}' | jq -r .id)
p4=$(post /api/products '{"name":"Notebook","hsnCode":"4820","unitPrice":60,"gstRate":12}' | jq -r .id)
p5=$(post /api/products '{"name":"Packed rice 5kg","hsnCode":"1006","unitPrice":410,"gstRate":5}' | jq -r .id)

n=0
for back in 330 300 270 240 210 180 150 120 90 60 45 30 20 10 5; do
  for cust in $c1 $c2 $c3 $c4; do
    n=$((n+1))
    now=$(date +%s); ie=$((now - (back - n % 4) * 86400)); issue=$(date -d "@$ie" +%F); due=$(date -d "@$((ie + 15 * 86400))" +%F)
    [ $((n % 3)) -eq 0 ] && pa=$p1 || pa=$p3
    body=$(jq -n --arg c "$cust" --arg no "DEMO-$(printf %04d $n)" --arg i "$issue" --arg d "$due" --arg pa "$pa" --arg pb "$p2" --arg pc "$p4" --argjson q $((n % 5 + 1)) \
      '{customerId:$c,invoiceNumber:$no,issueDate:$i,dueDate:$d,notes:"Demo invoice",lines:[{productId:$pa,quantity:$q},{productId:$pb,quantity:2},{productId:$pc,quantity:10}]}')
    id=$(post /api/invoices "$body" | jq -r .id)
    # leave the newest as drafts, issue the rest
    if [ $back -gt 5 ]; then
      total=$(post "/api/invoices/$id/issue" '{}' | jq -r .grandTotal)
      # older invoices are mostly paid (through the signed webhook, like a real provider), some part-paid, the rest stay open
      if [ $back -ge 60 ] && [ $((n % 4)) -ne 0 ]; then pay "$id" "$total"
      elif [ $back -ge 30 ] && [ $((n % 4)) -eq 0 ]; then pay "$id" "$(echo "$total" | awk '{printf "%.2f", $1 / 2}')"
      fi
    fi
  done
done
echo "Seeded 4 customers, 5 products and $n invoices. Login: $EMAIL"
