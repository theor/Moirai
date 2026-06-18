parser grammar MoiraiParser;

options {

  tokenVocab=moirai_lexer;

}
r: (def|LINE_BREAK)+ EOF;
def: attribute* (event|trigger|enum_definition|type_definition|function_definition); 
attribute: AT attr=ID (PAREN_OPEN expr (COMMA expr)* PAREN_CLOSE)? LINE_BREAK;
event: EVENT  ID scope;
trigger: TRIGGER ID scope;
when: WHEN type_id (AND expr)* SPACE* LINE_BREAK+;
when_created: WHEN_CREATED type_id (AND expr)* SPACE* LINE_BREAK+;
effect: (set | var | expr) SPACE* (LINE_BREAK)*;
if: IF cond=expr then=scope (ELSE LINE_BREAK*  else=scope)? ;
match: (MATCH|MATCH_WEIGHT) expr (COMMA expr)* SCOPE_OPEN LINE_BREAK* match_case+ SCOPE_CLOSE  LINE_BREAK*;
match_case: value (COMMA value)* ARROW ((effect LINE_BREAK+)|scope) ;
set: SET  path EQ expr;
var: VAR  VAR_ID COLON expr;
fun_id: ID;
//call_assign : ID (VAR_ID COLON)?  ((expr (COMMA expr)* )) scope?;
call : fun_id  (type VAR_ID COLON)? PAREN_OPEN ((expr (COMMA expr)* ))? PAREN_CLOSE scope?;
scope: SCOPE_OPEN LINE_BREAK* (when|when_created)?  ((effect SCOPE_CLOSE)|((effect LINE_BREAK+)* SCOPE_CLOSE)) LINE_BREAK*;
// 'raw' means no parens and one param - eg record 'asd'
raw_call: fun_id  ((type VAR_ID (COLON value)?)| value) scope?;
value: raw_call | call | string | enum_value | type_id | path | bool | number | NULL;
expr
    : if
    | match
    | value
    | left=expr op=(MUL | DIV | MOD) LINE_BREAK? right=expr
    | left=expr op=(ADD | SUB) LINE_BREAK? right=expr
    | left=expr op=(EQ | NEQ | GE | LE | GT | LT) LINE_BREAK? right=expr
    | left=expr op=QQ LINE_BREAK? right=expr
    | left=expr op=AND LINE_BREAK? right=expr
    | left=expr op=OR LINE_BREAK? right=expr
    | (PAREN_OPEN paren_expr=expr PAREN_CLOSE)
    ;

type_definition: ENTITY TYPE_ID SCOPE_OPEN LINE_BREAK* (prop_definition|function_definition)* SCOPE_CLOSE LINE_BREAK+ ;

//range: (PAREN_OPEN number COMMA number PAREN_CLOSE);
prop_definition: PROP property_id COLON type LINE_BREAK+ ;

enum_definition: ENUM TYPE_ID SCOPE_OPEN LINE_BREAK* TYPE_ID (COMMA LINE_BREAK* TYPE_ID)* COMMA? LINE_BREAK* SCOPE_CLOSE LINE_BREAK+ ;

param: VAR_ID COLON type;
function_definition: FUNCTION fun_id PAREN_OPEN (param (COMMA param)*)? PAREN_CLOSE (COLON type)? scope;

stringContent: (EXPR_OPEN expr SCOPE_CLOSE) | TEXT;
string: QUOTE stringContent* QUOTE ;

bool: TRUE | FALSE;
type_id: TYPE_ID;
type: TYPE_ID | ID | LBRACK (TYPE_ID | ID) RBRACK;
property_id: ID;
dot_property: DOT (property_id | call);
var_id_read: SINGLETON_ID | VAR_ID;
path : (var_id_read | property_id) dot_property*;
enum_value: TYPE_ID DOT TYPE_ID ;
number: NUMBER_FLOAT | NUMBER | PERCENT;
