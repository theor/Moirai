parser grammar MoiraiParser;
@header {
    namespace Moirai.Parser;
}
options {

  tokenVocab=moirai_lexer;

}
r: (event|trigger|prop_definition|enum_definition|type_definition|LINE_BREAK)+ EOF;
filter:AT (occurence=NUMBER ID years=NUMBER)? ID LINE_BREAK?;
event: filter? EVENT  ID categories scope;
categories: ID* ;
trigger: TRIGGER ID categories scope;
when: WHEN TYPE_ID (AND expr)* SPACE* LINE_BREAK+;
when_created: WHEN_CREATED TYPE_ID (AND expr)* SPACE* LINE_BREAK+;
effect: (set | var | expr) SPACE* (LINE_BREAK)*;
if: IF cond=expr then=scope (ELSE LINE_BREAK*  else=scope)? ;
match: (MATCH|MATCH_WEIGHT) expr (COMMA expr)* SCOPE_OPEN LINE_BREAK* match_case+ SCOPE_CLOSE  LINE_BREAK*;
match_case: value (COMMA value)* ARROW ((effect LINE_BREAK+)|scope) ;
set: SET  path EQ expr;
var: VAR  VAR_ID COLON expr;
//call_assign : ID (VAR_ID COLON)?  ((expr (COMMA expr)* )) scope?;
call : ID  (VAR_ID COLON)? PAREN_OPEN ((expr (COMMA expr)* )) PAREN_CLOSE scope?;
scope: SCOPE_OPEN LINE_BREAK* (when|when_created)?  ((effect SCOPE_CLOSE)|((effect LINE_BREAK+)* SCOPE_CLOSE)) LINE_BREAK*;
raw_call: ID  (VAR_ID COLON)? value scope?;
value: raw_call | call | string | enum_value | TYPE_ID | path | bool | number | NULL;
expr
    : if
    | match
    | value
    | left=expr op=(MUL | DIV) right=expr
    | left=expr op=(ADD | SUB) right=expr
    | left=expr op=(EQ | NEQ | GE | LE | GT | LT) right=expr
    | left=expr op=QQ right=expr
    | left=expr op=AND right=expr
    | left=expr op=OR right=expr
    | (PAREN_OPEN paren_expr=expr PAREN_CLOSE)
    ;

type_definition: ENTITY TYPE_ID (SCOPE_OPEN LINE_BREAK* SCOPE_CLOSE)? LINE_BREAK+ ;

//range: (PAREN_OPEN number COMMA number PAREN_CLOSE);
prop_definition: PROP ID COLON (ID|TYPE_ID) LINE_BREAK+ ;

enum_definition: ENUM TYPE_ID SCOPE_OPEN LINE_BREAK* TYPE_ID (COMMA LINE_BREAK* TYPE_ID)* COMMA? LINE_BREAK* SCOPE_CLOSE LINE_BREAK+ ;

string: STRING ;

bool: TRUE | FALSE;
path : (SINGLETON_ID | VAR_ID) (DOT ID)* | ID;
enum_value: TYPE_ID DOT TYPE_ID ;
number: NUMBER_FLOAT | NUMBER | PERCENT;
